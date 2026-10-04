using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Primitives;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// らじる★らじる録音用のソース取得実装
/// </summary>
public class RadiruRecordingSource(
    ILogger<RadiruRecordingSource> logger,
    ProgramScheduleLobLogic programScheduleLobLogic,
    StationLobLogic stationLobLogic) : IRecordingSource
{
    /// <summary>
    /// らじる★らじるを処理可能か判定する
    /// </summary>
    public bool CanHandle(RadioServiceKind kind) => kind == RadioServiceKind.Radiru;

    /// <summary>
    /// 録音に必要なストリームURLと番組情報を取得する
    /// </summary>
    public async ValueTask<RecordingSourceResult> PrepareAsync(RecordingCommand command, CancellationToken cancellationToken = default)
    {
        // 番組情報の取得
        var program = await programScheduleLobLogic.GetRadiruProgramAsync(command.ProgramId);
        if (program == null)
        {
            logger.ZLogWarning($"番組情報の取得に失敗しました。");
            throw new DomainException("番組情報の取得に失敗しました。");
        }

        var now = DateTimeOffset.UtcNow;
        var isOnDemand = command.IsOnDemand;

        // 通常録音は放送終了後に実行できない
        if (!isOnDemand && program.EndTime < now)
        {
            logger.ZLogWarning($"番組が放送終了しているため録音できません。");
            throw new DomainException("番組が放送終了しているため録音できません。");
        }

        string streamUrl;
        if (isOnDemand)
        {
            if (string.IsNullOrWhiteSpace(program.OnDemandContentUrl))
            {
                logger.ZLogWarning($"聞き逃し配信URLが存在しないため録音できません。 programId={program.ProgramId}");
                throw new DomainException("聞き逃し配信URLが見つかりません。番組表更新後に再度お試しください。");
            }

            if (!program.OnDemandExpiresAtUtc.HasValue || program.OnDemandExpiresAtUtc.Value <= DateTime.UtcNow)
            {
                logger.ZLogWarning($"聞き逃し配信の有効期限切れです。 programId={program.ProgramId} expiresUtc={program.OnDemandExpiresAtUtc}");
                throw new DomainException("聞き逃し配信の有効期限が切れています。");
            }

            streamUrl = program.OnDemandContentUrl;
        }
        else
        {
            streamUrl = await stationLobLogic.GetRadiruHlsUrlByAreaAndServiceAsync(
                program.AreaId,
                program.StationId,
                cancellationToken) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                logger.ZLogWarning($"らじる★らじるの放送局URLが解決できないため録音できません。 areaId={program.AreaId} stationId={program.StationId} programId={program.ProgramId}");
                throw new DomainException("放送局の判定ができませんでした。");
            }
        }

        var programInfo = new ProgramRecordingInfo(
            ProgramId: program.ProgramId,
            Title: program.Title,
            Subtitle: program.Subtitle,
            StationId: program.StationId,
            StationName: program.StationName,
            AreaId: program.AreaId,
            StartTime: program.StartTime,
            EndTime: program.EndTime,
            Performer: program.Performer,
            Description: program.Description,
            ProgramUrl: program.ProgramUrl,
            ImageUrl: program.ImageUrl);

        var options = new RecordingOptions(
            ServiceKind: RadioServiceKind.Radiru,
            IsTimeFree: false,
            IsOnDemand: isOnDemand,
            StartDelaySeconds: command.StartDelaySeconds,
            EndDelaySeconds: command.EndDelaySeconds,
            OutputDirectoryRelativePathOverride: command.OutputDirectoryRelativePathOverride,
            OutputFileNameTemplateOverride: command.OutputFileNameTemplateOverride);

        return new RecordingSourceResult(
            StreamUrl: streamUrl,
            Headers: new Dictionary<string, string>(),
            ProgramInfo: programInfo,
            Options: options,
            RequestStationIdOverride: null)
        { AcquisitionPlan = new(isOnDemand ? RecordingAcquisitionPlan.Archive : RecordingAcquisitionPlan.Live, FastStart: true) };
    }
}
