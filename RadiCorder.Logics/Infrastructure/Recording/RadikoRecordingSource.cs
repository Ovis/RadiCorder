using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// radiko録音用のソース取得実装
/// </summary>
public class RadikoRecordingSource(
    ILogger<RadikoRecordingSource> logger,
    ProgramScheduleLobLogic programScheduleLobLogic,
    StationLobLogic stationLobLogic,
    RadikoUniqueProcessLogic radikoUniqueProcessLogic,
    IRadikoApiClient radikoApiClient,
    IRadikoProxyTicketService radikoProxyTicketService,
    ILocalApplicationUrlService localApplicationUrlService,
    RadioDbContext dbContext) : IRecordingSource, IRecordingSourceRetryHandler
{
    /// <summary>
    /// radikoを処理可能か判定する
    /// </summary>
    public bool CanHandle(RadioServiceKind kind) => kind == RadioServiceKind.Radiko;

    /// <summary>
    /// 録音に必要なストリームURLと番組情報を取得する
    /// </summary>
    public async ValueTask<RecordingSourceResult> PrepareAsync(RecordingCommand command, CancellationToken cancellationToken = default)
    {
        // 番組情報の取得
        var program = await programScheduleLobLogic.GetRadikoProgramAsync(command.ProgramId);
        if (program == null)
        {
            throw new DomainException("番組情報の取得に失敗しました。");
        }

        // 現在エリア取得
        var (areaSuccess, area) = await radikoUniqueProcessLogic.GetRadikoAreaAsync(cancellationToken: cancellationToken);
        if (!areaSuccess)
        {
            throw new DomainException("エリア情報の取得に失敗しました。");
        }

        // ログインとエリアチェック
        var (_, session, _, isAreaFree) = await radikoUniqueProcessLogic.LoginRadikoAsync(cancellationToken: cancellationToken);
        var stationInformation = await dbContext.RadikoStations.FindAsync(program.StationId);
        var currentAreaStation = await stationLobLogic.GetCurrentAreaStations(area);
        if (stationInformation != null && !currentAreaStation.Contains(stationInformation.StationId) && !isAreaFree)
        {
            logger.ZLogError($"現在エリアと番組の視聴可能エリアが異なる。現在エリア{string.Join(',', currentAreaStation)} 番組視聴エリア {stationInformation.StationId}");
            throw new DomainException("この番組は地域が異なるため録音できません。プレミアム会員でのログインが必要です。");
        }

        var useAreaFreeConnection = stationInformation != null &&
                                    isAreaFree &&
                                    !currentAreaStation.Contains(stationInformation.StationId);

        // 認証トークン取得
        var (authSuccess, token, areaId, subStations) = await radikoUniqueProcessLogic.AuthorizeRadikoAsync(session, cancellationToken: cancellationToken);
        if (!authSuccess || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(areaId))
        {
            throw new DomainException("radiko認証に失敗しました。");
        }

        var requestStationId = RadikoUniqueProcessLogic.ResolveSubStationId(areaId, program.StationId, subStations) ?? program.StationId;

        var headers = new Dictionary<string, string>
        {
            { "X-Radiko-Authtoken", token }
        };

        // タイムフリーとリアルタイムでURLを切り替える
        string streamUrl;
        if (command.IsTimeFree)
        {
            var urls = await radikoApiClient.GetTimeFreePlaylistCreateUrlsAsync(program.StationId, useAreaFreeConnection, cancellationToken);
            if (urls.Count == 0 && useAreaFreeConnection)
            {
                // areafree URLが取得できない場合はfallbackで通常URLを試す
                urls = await radikoApiClient.GetTimeFreePlaylistCreateUrlsAsync(program.StationId, false, cancellationToken);
            }

            if (urls.Count == 0)
            {
                throw new DomainException("タイムフリー録音URLの取得に失敗しました。");
            }

            streamUrl = urls[0];
        }
        else
        {
            var urls = await radikoApiClient.GetRealTimePlaylistUrlsAsync(program.StationId, useAreaFreeConnection, requestStationId, cancellationToken);
            if (urls.Count == 0 && useAreaFreeConnection)
            {
                // areafree URLが取得できない場合はfallbackで通常URLを試す
                urls = await radikoApiClient.GetRealTimePlaylistUrlsAsync(program.StationId, false, requestStationId, cancellationToken);
            }

            if (urls.Count == 0)
            {
                throw new DomainException("リアルタイム録音URLの取得に失敗しました。");
            }

            var resolvedUrl = urls[0];
            var localBaseUrl = localApplicationUrlService.GetBaseUrl();
            var proxyKey = radikoProxyTicketService.IssueTokenTicket(token);
            streamUrl = string.IsNullOrWhiteSpace(localBaseUrl)
                ? resolvedUrl
                : RadikoProxyUrlUtility.BuildAbsoluteProxyUrlWithProxyKey(
                    localBaseUrl,
                    resolvedUrl,
                    proxyKey,
                    resolveLivePlaylist: true,
                    recordingStartUtc: program.StartTime);
        }

        var programInfo = new ProgramRecordingInfo(
            ProgramId: program.ProgramId,
            Title: program.Title,
            Subtitle: program.Subtitle,
            StationId: program.StationId,
            StationName: program.StationName,
            AreaId: area,
            StartTime: program.StartTime,
            EndTime: program.EndTime,
            Performer: program.Performer,
            Description: program.Description,
            ProgramUrl: program.ProgramUrl,
            ImageUrl: program.ImageUrl);

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiko,
            IsTimeFree: command.IsTimeFree,
            IsOnDemand: false,
            StartDelaySeconds: command.StartDelaySeconds,
            EndDelaySeconds: command.EndDelaySeconds,
            OutputDirectoryRelativePathOverride: command.OutputDirectoryRelativePathOverride,
            OutputFileNameTemplateOverride: command.OutputFileNameTemplateOverride);

        return new RecordingSourceResult(
            StreamUrl: streamUrl,
            Headers: headers,
            ProgramInfo: programInfo,
            Options: options,
            RequestStationIdOverride: requestStationId);
    }

    /// <summary>
    /// タイムフリー録音の再試行前に認証キャッシュを破棄する。
    /// </summary>
    public ValueTask PrepareForRetryAsync(RecordingCommand command, CancellationToken cancellationToken = default)
    {
        if (!command.IsTimeFree)
        {
            return ValueTask.CompletedTask;
        }

        logger.ZLogWarning($"タイムフリー録音の再試行前に radiko 認証キャッシュを破棄します。 programId={command.ProgramId}");
        radikoUniqueProcessLogic.InvalidateAuthenticationCache();
        return ValueTask.CompletedTask;
    }
}
