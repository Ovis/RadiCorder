using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Logics.PlayProgramLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Features.Program;

/// <summary>
/// 番組関連の Api エンドポイントを提供する。
/// </summary>
public static class ProgramEndpoints
{
    private static readonly TimeSpan LivePlaylistProgramDateTimeLookback = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan LivePlaylistStartSyncTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan LivePlaylistStartSyncPollInterval = TimeSpan.FromSeconds(1);
    private const int LivePlaylistFallbackSegmentCount = 3;
    private const int LivePlaylistLiveEdgeSegmentCount = 2;

    /// <summary>
    /// 番組関連エンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapProgramEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/programs").WithTags("ProgramsApi");
        group.MapGet("/stations/radiko", HandleGetRadikoStationsAsync)
            .WithName("ApiProgramStationsRadiko")
            .WithSummary("radiko放送局一覧を取得する");
        group.MapGet("/stations/radiru", HandleGetRadiruStationsAsync)
            .WithName("ApiProgramStationsRadiru")
            .WithSummary("らじる放送局一覧を取得する");
        group.MapGet("/list/radiko", HandleGetRadikoProgramsAsync)
            .WithName("ApiProgramListRadiko")
            .WithSummary("radiko番組一覧を取得する");
        group.MapGet("/list/radiru", HandleGetRadiruProgramsAsync)
            .WithName("ApiProgramListRadiru")
            .WithSummary("らじる番組一覧を取得する");
        group.MapGet("/now", HandleGetNowOnAirProgramsAsync)
            .WithName("ApiProgramNow")
            .WithSummary("現在放送中の番組を取得する");
        group.MapGet("/detail", HandleGetProgramDetailAsync)
            .WithName("ApiProgramDetail")
            .WithSummary("番組詳細を取得する");
        group.MapPost("/search", HandleSearchProgramsAsync)
            .WithName("ApiProgramSearch")
            .WithSummary("番組を検索する");
        group.MapPost("/keyword-reserve", HandleSetKeywordReserveAsync)
            .WithName("ApiProgramKeywordReserve")
            .WithSummary("キーワード予約を登録する");
        group.MapPost("/update", HandleUpdateProgramsAsync)
            .WithName("ApiProgramUpdate")
            .WithSummary("番組表更新ジョブを起動する");
        group.MapGet("/update-status", HandleGetProgramUpdateStatus)
            .WithName("ApiProgramUpdateStatus")
            .WithSummary("番組表更新状態を取得する");
        group.MapPost("/reserve", HandleReserveProgramAsync)
            .WithName("ApiProgramReserve")
            .WithSummary("番組録音予約を登録する");
        group.MapPost("/play", HandlePlayProgramAsync)
            .WithName("ApiProgramPlay")
            .WithSummary("番組再生情報を取得する");
        group.MapGet("/radiko-proxy", HandleRadikoProxyAsync)
            .WithName("ApiProgramRadikoProxy")
            .WithSummary("radiko HLS を同一オリジン経由で中継する");
        group.MapGet("/radiko-proxy/{*hint}", HandleRadikoProxyAsync)
            .WithName("ApiProgramRadikoProxyWithHint")
            .WithSummary("radiko HLS を同一オリジン経由で中継する");
        return endpoints;
    }

    /// <summary>
    /// 現在エリアのradiko放送局ID一覧を取得する。
    /// </summary>
    private static async ValueTask<List<string>> GetCurrentAreaStationsAsync(
        ILogger<ProgramEndpointsMarker> logger,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic,
        StationLobLogic stationLobLogic)
    {
        var (isSuccess, area) = await radikoUniqueProcessLogic.GetRadikoAreaAsync();
        if (!isSuccess || string.IsNullOrWhiteSpace(area))
        {
            logger.ZLogWarning($"radikoエリア情報の取得に失敗");
            return [];
        }

        return await stationLobLogic.GetCurrentAreaStations(area);
    }

    /// <summary>
    /// radiko放送局一覧を取得する。
    /// </summary>
    private static async Task<Ok<ApiResponse<Dictionary<string, List<RadikoStationInformationEntry>>>>> HandleGetRadikoStationsAsync(
        StationLobLogic stationLobLogic)
    {
        var stations = (await stationLobLogic.GetRadikoStationAsync())
            .GroupBy(r => r.RegionId)
            .OrderBy(r => r.First().RegionOrder)
            .ToDictionary(r => r.First().RegionName, r => r.OrderBy(s => s.StationOrder).ToList());
        return TypedResults.Ok(ApiResponse.Ok(stations));
    }

    /// <summary>
    /// らじる放送局一覧を取得する。
    /// </summary>
    private static async Task<Ok<ApiResponse<Dictionary<string, List<RadiruStationEntry>>>>> HandleGetRadiruStationsAsync(
        StationLobLogic stationLobLogic)
    {
        var stations = (await stationLobLogic.GetRadiruStationAsync())
            .GroupBy(r => r.AreaName)
            .ToDictionary(group => group.Key, group => group.ToList());
        return TypedResults.Ok(ApiResponse.Ok(stations));
    }

    /// <summary>
    /// radiko番組一覧を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<List<RadioProgramEntry>>>, BadRequest<ApiResponse<EmptyData?>>>> HandleGetRadikoProgramsAsync(
        ILogger<ProgramEndpointsMarker> logger,
        IAppConfigurationService config,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic,
        StationLobLogic stationLobLogic,
        ProgramScheduleLobLogic programScheduleLobLogic,
        string d,
        string s)
    {
        if (!DateOnly.TryParseExact(d, "yyyyMMdd", null, DateTimeStyles.None, out var date))
        {
            logger.ZLogWarning($"日付の変換に失敗 {d}");
            return TypedResults.BadRequest(ApiResponse.Fail("日付の変換に失敗しました。"));
        }

        if (!config.IsRadikoAreaFree)
        {
            var currentAreaStations = await GetCurrentAreaStationsAsync(logger, radikoUniqueProcessLogic, stationLobLogic);
            var currentAreaStationSet = currentAreaStations.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (currentAreaStationSet.Count == 0 || !currentAreaStationSet.Contains(s))
            {
                return TypedResults.Ok(ApiResponse.Ok(new List<RadioProgramEntry>()));
            }
        }

        var programs = await programScheduleLobLogic.GetRadikoProgramListAsync(date, s);
        return TypedResults.Ok(ApiResponse.Ok(programs));
    }

    /// <summary>
    /// らじる番組一覧を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<List<RadioProgramEntry>>>, BadRequest<ApiResponse<EmptyData?>>>> HandleGetRadiruProgramsAsync(
        ILogger<ProgramEndpointsMarker> logger,
        ProgramScheduleLobLogic programScheduleLobLogic,
        string d,
        string s,
        string a)
    {
        if (!DateOnly.TryParseExact(d, "yyyyMMdd", null, DateTimeStyles.None, out var date))
        {
            logger.ZLogWarning($"日付の変換に失敗 {d}");
            return TypedResults.BadRequest(ApiResponse.Fail("日付の変換に失敗しました。"));
        }

        var programs = await programScheduleLobLogic.GetRadiruProgramAsync(date, a, s);
        return TypedResults.Ok(ApiResponse.Ok(programs));
    }

    /// <summary>
    /// 現在放送中の番組一覧を取得する。
    /// </summary>
    private static async Task<Ok<ApiResponse<ProgramNowOnAirResponse>>> HandleGetNowOnAirProgramsAsync(
        ILogger<ProgramEndpointsMarker> logger,
        IRadioAppContext appContext,
        IAppConfigurationService config,
        StationLobLogic stationLobLogic,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic,
        ProgramScheduleLobLogic programScheduleLobLogic)
    {
        var radikoPrograms = await programScheduleLobLogic.GetRadikoNowOnAirProgramListAsync(appContext.StandardDateTimeOffset);
        var radiruPrograms = await programScheduleLobLogic.GetRadiruNowOnAirProgramListAsync(appContext.StandardDateTimeOffset);

        var programs = radikoPrograms.Concat(radiruPrograms).OrderBy(r => r.StationName).ToList();
        List<string> currentAreaStationsForUi = [];

        var stationList = await stationLobLogic.GetAllRadikoStationAsync(activeOnly: false);
        var stationById = stationList.ToDictionary(r => r.StationId, r => r);
        var regionOrderMap = stationList
            .GroupBy(r => r.RegionId)
            .ToDictionary(r => r.Key, r => new { RegionName = r.First().RegionName, RegionOrder = r.Min(s => s.RegionOrder) });

        foreach (var program in programs)
        {
            if (stationById.TryGetValue(program.StationId, out var station))
            {
                program.AreaId = station.RegionId;
                program.AreaName = station.RegionName;
            }
        }

        currentAreaStationsForUi = await GetCurrentAreaStationsAsync(logger, radikoUniqueProcessLogic, stationLobLogic);
        if (!config.IsRadikoAreaFree)
        {
            var currentAreaStationSet = currentAreaStationsForUi.ToHashSet(StringComparer.OrdinalIgnoreCase);
            programs = programs
                .Where(p => p.ServiceKind == RadioServiceKind.Radiru || currentAreaStationSet.Contains(p.StationId))
                .ToList();
        }

        var radiruAreaOrderMap = Enum
            .GetValues<RadiruAreaKind>()
            .Select((area, index) => new { AreaId = area.GetEnumCodeId(), AreaOrder = index })
            .ToDictionary(x => x.AreaId, x => x.AreaOrder);

        var areaMap = new Dictionary<string, ProgramAreaEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var program in programs)
        {
            if (string.IsNullOrWhiteSpace(program.AreaId) || string.IsNullOrWhiteSpace(program.AreaName))
            {
                continue;
            }

            var key = $"{(int)program.ServiceKind}:{program.AreaId}";
            if (areaMap.ContainsKey(key))
            {
                continue;
            }

            if (program.ServiceKind == RadioServiceKind.Radiko && regionOrderMap.TryGetValue(program.AreaId, out var region))
            {
                areaMap[key] = new ProgramAreaEntry(program.AreaId, region.RegionName, region.RegionOrder, 0);
                continue;
            }

            if (program.ServiceKind == RadioServiceKind.Radiru && radiruAreaOrderMap.TryGetValue(program.AreaId, out var radiruOrder))
            {
                areaMap[key] = new ProgramAreaEntry(program.AreaId, program.AreaName, radiruOrder, 1);
                continue;
            }

            areaMap[key] = new ProgramAreaEntry(program.AreaId, program.AreaName, int.MaxValue, 9);
        }

        var areaList = areaMap.Values
            .OrderBy(r => r.ServiceOrder)
            .ThenBy(r => r.AreaOrder)
            .ThenBy(r => r.AreaName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var data = new ProgramNowOnAirResponse(
            programs,
            areaList,
            currentAreaStationsForUi,
            config.IsRadikoAreaFree);
        return TypedResults.Ok(ApiResponse.Ok(data));
    }

    /// <summary>
    /// 番組詳細を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<RadioProgramEntry?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleGetProgramDetailAsync(
        ProgramScheduleLobLogic programScheduleLobLogic,
        string? id,
        string? kind)
    {
        if (id == null || kind == null)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("必要な項目が記載されていません。"));
        }

        var radioServiceKind = kind.GetEnumByCodeId<RadioServiceKind>();
        if (radioServiceKind == RadioServiceKind.Undefined)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("サービス種別が不正です。"));
        }

        var program = await programScheduleLobLogic.GetProgramAsync(id, radioServiceKind);
        return TypedResults.Ok(ApiResponse.Ok(program));
    }

    /// <summary>
    /// 番組検索を実行する。
    /// </summary>
    private static async Task<Ok<ApiResponse<IEnumerable<ProgramForApiEntry>>>> HandleSearchProgramsAsync(
        ILogger<ProgramEndpointsMarker> logger,
        IAppConfigurationService config,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic,
        StationLobLogic stationLobLogic,
        ProgramScheduleLobLogic programScheduleLobLogic,
        IEntryMapper entryMapper,
        ProgramSearchEntity entity)
    {
        var radikoResults = new List<RadikoProgram>();
        var radiruResults = new List<NhkRadiruProgram>();

        if (entity.SelectedRadikoStationIds.Any())
        {
            if (!config.IsRadikoAreaFree)
            {
                var currentAreaStations = await GetCurrentAreaStationsAsync(logger, radikoUniqueProcessLogic, stationLobLogic);
                var currentAreaStationSet = currentAreaStations.ToHashSet(StringComparer.OrdinalIgnoreCase);
                entity.SelectedRadikoStationIds = entity.SelectedRadikoStationIds
                    .Where(id => currentAreaStationSet.Contains(id))
                    .ToList();
            }

            if (entity.SelectedRadikoStationIds.Any())
            {
                radikoResults = await programScheduleLobLogic.SearchRadikoProgramAsync(entity);
            }
        }

        if (entity.SelectedRadiruStationIds.Any())
        {
            var visibleRadiruStationIds = (await stationLobLogic.GetRadiruStationAsync())
                .Select(x => $"{x.AreaId}:{x.StationId}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            entity.SelectedRadiruStationIds = entity.SelectedRadiruStationIds
                .Where(id => visibleRadiruStationIds.Contains(id))
                .ToList();

            if (entity.SelectedRadiruStationIds.Any())
            {
                radiruResults = await programScheduleLobLogic.SearchRadiruProgramAsync(entity);
            }
        }

        var sortedRadiko = SortSearchResults(
            radikoResults,
            entity.OrderKind,
            x => x.StartTime,
            x => x.EndTime,
            x => x.Title);
        var sortedRadiru = SortSearchResults(
            radiruResults,
            entity.OrderKind,
            x => x.StartTime,
            x => x.EndTime,
            x => x.Title);

        var result = entity.OrderKind switch
        {
            KeywordReserveOrderKind.ProgramStartDateTimeAsc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => left.StartTime.CompareTo(right.StartTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramStartDateTimeDesc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => right.StartTime.CompareTo(left.StartTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramEndDateTimeAsc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => left.EndTime.CompareTo(right.EndTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramEndDateTimeDesc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => right.EndTime.CompareTo(left.EndTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramNameAsc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => StringComparer.Ordinal.Compare(left.Title, right.Title),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramNameDesc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => StringComparer.Ordinal.Compare(right.Title, left.Title),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            _ => TakeConcatenatedSearchResults(
                radikoResults,
                radiruResults,
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right))
        };

        return TypedResults.Ok(ApiResponse.Ok<IEnumerable<ProgramForApiEntry>>(result));
    }

    /// <summary>
    /// キーワード予約を登録する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleSetKeywordReserveAsync(
        ReserveLobLogic reserveLobLogic,
        KeywordReserveEntry entry)
    {
        if (!Enum.IsDefined(typeof(KeywordReserveTagMergeBehavior), entry.MergeTagBehavior))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("タグマージ設定が不正です。"));
        }

        if (!string.IsNullOrEmpty(entry.RecordPath) && !entry.RecordPath.IsValidRelativePath())
        {
            return TypedResults.BadRequest(ApiResponse.Fail("保存先パスが不正です。"));
        }

        if (!string.IsNullOrEmpty(entry.RecordFileName) && !entry.RecordFileName.IsValidFileName())
        {
            return TypedResults.BadRequest(ApiResponse.Fail("ファイル名が不正です。"));
        }

        var (isSuccess, error) = await reserveLobLogic.SetKeywordReserveAsync(entry);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "キーワード予約の登録に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("登録しました。"));
    }

    /// <summary>
    /// 番組表更新ジョブを起動する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateProgramsAsync(
        ILogger<ProgramEndpointsMarker> logger,
        IServiceScopeFactory serviceScopeFactory)
    {
        try
        {
            // API は即時応答し、番組表更新本体は独立スコープでバックグラウンド実行する。
            // スコープを作り直して、リクエスト終了後の破棄済み DbContext 参照を防ぐ。
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        using var scope = serviceScopeFactory.CreateScope();
                        var programUpdateRunner = scope.ServiceProvider.GetRequiredService<ProgramUpdateRunner>();
                        await programUpdateRunner.ExecuteAsync("manual");
                    }
                    catch (Exception ex)
                    {
                        logger.ZLogError(ex, $"番組表更新バックグラウンド実行でエラーが発生しました。");
                    }
                });
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"番組表更新処理の起動に失敗しました。");
            return TypedResults.BadRequest(ApiResponse.Fail("番組表更新ジョブの起動に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("番組表更新処理を実行中です。通常数分で更新が完了します。"));
    }

    /// <summary>
    /// 番組表更新状態を取得する。
    /// </summary>
    private static Ok<ApiResponse<ProgramUpdateStatusResponse>> HandleGetProgramUpdateStatus(
        IProgramUpdateStatusService programUpdateStatusService)
    {
        var status = programUpdateStatusService.GetCurrent();
        var response = new ProgramUpdateStatusResponse(
            status.IsRunning,
            status.TriggerSource,
            status.Message,
            status.StartedAtUtc,
            status.LastCompletedAtUtc,
            status.LastSucceeded);
        return TypedResults.Ok(ApiResponse.Ok(response));
    }

    /// <summary>
    /// 番組の録音予約を登録する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleReserveProgramAsync(
        ReserveLobLogic reserveLobLogic,
        ProgramInformationRequestEntry program)
    {
        (bool isSuccess, Exception? error) result = program.RadioServiceKind switch
        {
            RadioServiceKind.Radiko => await reserveLobLogic.SetRecordingJobByProgramIdAsync(program.ProgramId, RadioServiceKind.Radiko, program.RecordingType),
            RadioServiceKind.Radiru => await reserveLobLogic.SetRecordingJobByProgramIdAsync(program.ProgramId, RadioServiceKind.Radiru, program.RecordingType),
            _ => (false, new InvalidOperationException($"サービス種別が不正です。 {program.RadioServiceKind}"))
        };

        if (!result.isSuccess)
        {
            var message = program.RadioServiceKind is RadioServiceKind.Other or RadioServiceKind.Undefined
                ? "サービス種別が不正です。"
                : result.error?.Message ?? "録音予約に失敗しました。";
            return TypedResults.BadRequest(ApiResponse.Fail(message));
        }

        return TypedResults.Ok(ApiResponse.Ok("予約しました。"));
    }

    /// <summary>
    /// 番組再生情報を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<ProgramPlaybackInfoResponse>>, BadRequest<ApiResponse<EmptyData?>>>> HandlePlayProgramAsync(
        PlayProgramLobLogic playProgramLobLogic,
        IRadikoProxyTicketService radikoProxyTicketService,
        ProgramInformationRequestEntry program)
    {
        switch (program.RadioServiceKind)
        {
            case RadioServiceKind.Radiko:
            {
                var (isSuccess, token, url, error) = await playProgramLobLogic.PlayRadikoProgramAsync(program.ProgramId);
                if (!isSuccess)
                {
                    return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "再生準備に失敗しました。"));
                }

                var proxyKey = radikoProxyTicketService.IssueTokenTicket(token!);
                var proxiedUrl = RadikoProxyUrlUtility.BuildRelativeProxyUrlWithProxyKey(url!, proxyKey);
                return TypedResults.Ok(ApiResponse.Ok(new ProgramPlaybackInfoResponse(null, proxiedUrl)));
            }
            case RadioServiceKind.Radiru:
            {
                var (isSuccess, token, url, error) = await playProgramLobLogic.PlayRadiruProgramAsync(program.ProgramId);
                if (!isSuccess)
                {
                    return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "番組の再生ができませんでした。"));
                }

                return TypedResults.Ok(ApiResponse.Ok(new ProgramPlaybackInfoResponse(token, url)));
            }
            default:
                return TypedResults.BadRequest(ApiResponse.Fail("サービス種別が不正です。"));
        }
    }

    /// <summary>
    /// radiko の HLS を同一オリジン経由で配信する。
    /// </summary>
    private static async Task<IResult> HandleRadikoProxyAsync(
        ILogger<ProgramEndpointsMarker> logger,
        IHttpClientFactory httpClientFactory,
        IAppConfigurationService config,
        IRadikoProxyTicketService radikoProxyTicketService,
        string target,
        string? token,
        string? proxyKey,
        bool? resolveLivePlaylist,
        DateTimeOffset? recordingStartUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target) || (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(proxyKey)))
        {
            return Results.BadRequest("target and proxyKey/token are required.");
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var targetUri) || !IsAllowedRadikoProxyTarget(targetUri))
        {
            return Results.BadRequest("Invalid proxy target.");
        }

        var resolvedToken = ResolveProxyToken(radikoProxyTicketService, token, proxyKey);
        if (string.IsNullOrWhiteSpace(resolvedToken))
        {
            return Results.BadRequest("Invalid proxy credential.");
        }

        var effectiveProxyKey = !string.IsNullOrWhiteSpace(proxyKey)
            ? proxyKey!
            : radikoProxyTicketService.IssueTokenTicket(resolvedToken);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientNames.Radiko);
            if (resolveLivePlaylist == true)
            {
                var (resolvedPlaylist, playlistBaseUri, statusCode) = await ResolveLivePlaylistAsync(
                    logger,
                    client,
                    config,
                    targetUri,
                    resolvedToken,
                    recordingStartUtc,
                    cancellationToken);
                if (resolvedPlaylist == null || playlistBaseUri == null)
                {
                    logger.ZLogWarning($"radiko live proxy upstream failed. status={statusCode} target={targetUri}");
                    return Results.StatusCode(statusCode);
                }

                var rewritten = RewritePlaylistToLocalProxy(resolvedPlaylist, playlistBaseUri, effectiveProxyKey);
                return Results.Content(rewritten, "application/vnd.apple.mpegurl");
            }

            var response = await SendRadikoProxyRequestAsync(client, config, targetUri, resolvedToken, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                logger.ZLogWarning($"radiko proxy upstream failed. status={(int)response.StatusCode} target={targetUri}");
                return Results.StatusCode((int)response.StatusCode);
            }

            if (IsPlaylistRequest(targetUri, response.Content.Headers.ContentType?.MediaType))
            {
                using (response)
                {
                    var playlist = await response.Content.ReadAsStringAsync(cancellationToken);
                    var rewritten = RewritePlaylistToLocalProxy(playlist, targetUri, effectiveProxyKey);
                    return Results.Content(rewritten, "application/vnd.apple.mpegurl");
                }
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            return Results.Stream(
                async outputStream =>
                {
                    using (response)
                    {
                    await using var upstreamStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    await upstreamStream.CopyToAsync(outputStream, cancellationToken);
                    }
                },
                contentType);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"radiko proxy failed. target={targetUri}");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// ProgramEndpoints 用のロガーカテゴリ型。
    /// </summary>
    private sealed class ProgramEndpointsMarker;

    /// <summary>
    /// エリア一覧表示用の内部モデル。
    /// </summary>
    private sealed record ProgramAreaEntry(string AreaId, string AreaName, int AreaOrder, int ServiceOrder);

    private static List<T> SortSearchResults<T>(
        IEnumerable<T> source,
        KeywordReserveOrderKind orderKind,
        Func<T, DateTimeOffset> startSelector,
        Func<T, DateTimeOffset> endSelector,
        Func<T, string?> titleSelector)
    {
        return orderKind switch
        {
            KeywordReserveOrderKind.ProgramStartDateTimeAsc => source.OrderBy(startSelector).ToList(),
            KeywordReserveOrderKind.ProgramStartDateTimeDesc => source.OrderByDescending(startSelector).ToList(),
            KeywordReserveOrderKind.ProgramEndDateTimeAsc => source.OrderBy(endSelector).ToList(),
            KeywordReserveOrderKind.ProgramEndDateTimeDesc => source.OrderByDescending(endSelector).ToList(),
            KeywordReserveOrderKind.ProgramNameAsc => source.OrderBy(x => titleSelector(x) ?? string.Empty, StringComparer.Ordinal).ToList(),
            KeywordReserveOrderKind.ProgramNameDesc => source.OrderByDescending(x => titleSelector(x) ?? string.Empty, StringComparer.Ordinal).ToList(),
            _ => source.OrderBy(startSelector).ToList()
        };
    }

    private static List<ProgramForApiEntry> MergeSortedSearchResults<TLeft, TRight>(
        IReadOnlyList<TLeft> left,
        IReadOnlyList<TRight> right,
        Func<TLeft, TRight, int> compare,
        Func<TLeft, ProgramForApiEntry> leftMapper,
        Func<TRight, ProgramForApiEntry> rightMapper,
        int take = 100)
    {
        var result = new List<ProgramForApiEntry>(Math.Min(take, left.Count + right.Count));
        var leftIndex = 0;
        var rightIndex = 0;

        while (result.Count < take && (leftIndex < left.Count || rightIndex < right.Count))
        {
            if (leftIndex >= left.Count)
            {
                result.Add(rightMapper(right[rightIndex++]));
                continue;
            }

            if (rightIndex >= right.Count)
            {
                result.Add(leftMapper(left[leftIndex++]));
                continue;
            }

            if (compare(left[leftIndex], right[rightIndex]) <= 0)
            {
                result.Add(leftMapper(left[leftIndex++]));
            }
            else
            {
                result.Add(rightMapper(right[rightIndex++]));
            }
        }

        return result;
    }

    private static List<ProgramForApiEntry> TakeConcatenatedSearchResults<TLeft, TRight>(
        IReadOnlyList<TLeft> left,
        IReadOnlyList<TRight> right,
        Func<TLeft, ProgramForApiEntry> leftMapper,
        Func<TRight, ProgramForApiEntry> rightMapper,
        int take = 100)
    {
        var result = new List<ProgramForApiEntry>(Math.Min(take, left.Count + right.Count));

        foreach (var item in left)
        {
            if (result.Count >= take)
            {
                return result;
            }

            result.Add(leftMapper(item));
        }

        foreach (var item in right)
        {
            if (result.Count >= take)
            {
                return result;
            }

            result.Add(rightMapper(item));
        }

        return result;
    }

    /// <summary>
    /// 現在放送中番組一覧レスポンス。
    /// </summary>
    private sealed record ProgramNowOnAirResponse(
        List<RadioProgramEntry> Programs,
        List<ProgramAreaEntry> Areas,
        List<string> CurrentAreaStations,
        bool IsAreaFree);

    /// <summary>
    /// 再生開始に必要な情報レスポンス。
    /// </summary>
    private sealed record ProgramPlaybackInfoResponse(string? Token, string? Url);

    /// <summary>
    /// 番組表更新状態レスポンス。
    /// </summary>
    private sealed record ProgramUpdateStatusResponse(
        bool IsRunning,
        string? TriggerSource,
        string Message,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? LastCompletedAtUtc,
        bool? LastSucceeded);

    private static bool IsAllowedRadikoProxyTarget(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host;
        return host.Equals("radiko.jp", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".radiko.jp", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".smartstream.ne.jp", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".radiko-cf.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPlaylistRequest(Uri targetUri, string? mediaType)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
             mediaType.Contains("vnd.apple.mpegurl", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return targetUri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
    }

    private static string RewritePlaylistToLocalProxy(string content, Uri baseUri, string proxyKey)
    {
        var normalized = content.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var isMasterPlaylist = normalized.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                lines[i] = RewriteTagLineUris(line, baseUri, proxyKey);
                continue;
            }

            lines[i] = isMasterPlaylist
                ? RadikoProxyUrlUtility.BuildRelativeProxyUrlWithProxyKey(baseUri.ToString(), proxyKey, resolveLivePlaylist: true)
                : RadikoProxyUrlUtility.BuildRelativeProxyUrlWithProxyKey(new Uri(baseUri, line).ToString(), proxyKey);
        }

        return string.Join('\n', lines);
    }

    private static string RewriteTagLineUris(string line, Uri baseUri, string proxyKey)
    {
        return Regex.Replace(
            line,
            "URI=\"([^\"]+)\"",
            match =>
            {
                var value = match.Groups[1].Value;
                var resolved = new Uri(baseUri, value).ToString();
                var proxied = RadikoProxyUrlUtility.BuildRelativeProxyUrlWithProxyKey(resolved, proxyKey);
                return $"URI=\"{proxied}\"";
            });
    }

    private static string? ResolveProxyToken(
        IRadikoProxyTicketService radikoProxyTicketService,
        string? token,
        string? proxyKey)
    {
        if (!string.IsNullOrWhiteSpace(proxyKey))
        {
            return radikoProxyTicketService.TryGetToken(proxyKey, out var resolvedToken)
                ? resolvedToken
                : null;
        }

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static async Task<HttpResponseMessage> SendRadikoProxyRequestAsync(
        HttpClient client,
        IAppConfigurationService config,
        Uri targetUri,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
        request.Headers.TryAddWithoutValidation("X-Radiko-Authtoken", token);
        request.Headers.TryAddWithoutValidation("User-Agent", config.ExternalServiceUserAgent);
        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static async Task<(string? Playlist, Uri? PlaylistBaseUri, int StatusCode)> ResolveLivePlaylistAsync(
        ILogger<ProgramEndpointsMarker> logger,
        HttpClient client,
        IAppConfigurationService config,
        Uri targetUri,
        string token,
        DateTimeOffset? recordingStartUtc,
        CancellationToken cancellationToken)
    {
        using var upstreamResponse = await SendRadikoProxyRequestAsync(client, config, targetUri, token, cancellationToken);
        if (!upstreamResponse.IsSuccessStatusCode)
        {
            return (null, null, (int)upstreamResponse.StatusCode);
        }

        var upstreamContent = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!upstreamContent.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
        {
            logger.ZLogDebug($"radiko live proxy target is already media playlist. target={targetUri}");
            return (upstreamContent, targetUri, StatusCodes.Status200OK);
        }

        var mediaPlaylistUri = ExtractFirstPlaylistUri(upstreamContent, targetUri);
        if (mediaPlaylistUri == null)
        {
            return (null, null, StatusCodes.Status502BadGateway);
        }

        logger.ZLogDebug($"radiko live proxy resolved media playlist. master={targetUri} media={mediaPlaylistUri}");

        string? mediaPlaylist = null;
        var syncAttempt = 0;
        var syncDeadlineUtc = recordingStartUtc.HasValue
            ? DateTimeOffset.UtcNow.Add(LivePlaylistStartSyncTimeout)
            : (DateTimeOffset?)null;

        while (true)
        {
            syncAttempt++;
            using var mediaPlaylistResponse = await SendRadikoProxyRequestAsync(client, config, mediaPlaylistUri, token, cancellationToken);
            if (!mediaPlaylistResponse.IsSuccessStatusCode)
            {
                return (null, null, (int)mediaPlaylistResponse.StatusCode);
            }

            mediaPlaylist = await mediaPlaylistResponse.Content.ReadAsStringAsync(cancellationToken);
            if (recordingStartUtc is null)
            {
                break;
            }

            var parseResult = ParseLiveMediaPlaylist(mediaPlaylist);
            var lastSegmentEndUtc = parseResult.SegmentBlocks.LastOrDefault()?.EndTimeUtc;
            if (lastSegmentEndUtc is { } lastEndUtc && lastEndUtc >= recordingStartUtc.Value)
            {
                logger.ZLogDebug(
                    $"radiko live playlist start sync reached target. targetStartUtc={recordingStartUtc:O} lastSegmentEndUtc={lastEndUtc:O} attempt={syncAttempt} media={mediaPlaylistUri}");
                break;
            }

            if (syncDeadlineUtc.HasValue && DateTimeOffset.UtcNow >= syncDeadlineUtc.Value)
            {
                logger.ZLogWarning(
                    $"radiko live playlist start sync timed out. targetStartUtc={recordingStartUtc:O} lastSegmentEndUtc={lastSegmentEndUtc:O} attempt={syncAttempt} media={mediaPlaylistUri}");
                break;
            }

            logger.ZLogDebug(
                $"radiko live playlist start sync waiting. targetStartUtc={recordingStartUtc:O} lastSegmentEndUtc={lastSegmentEndUtc:O} attempt={syncAttempt} media={mediaPlaylistUri}");
            await Task.Delay(LivePlaylistStartSyncPollInterval, cancellationToken);
        }

        mediaPlaylist = TrimLiveMediaPlaylistForRecording(
            mediaPlaylist ?? string.Empty,
            DateTimeOffset.UtcNow,
            logger,
            recordingStartUtc);
        return (mediaPlaylist, mediaPlaylistUri, StatusCodes.Status200OK);
    }

    private static Uri? ExtractFirstPlaylistUri(string playlist, Uri baseUri)
    {
        var normalized = playlist.Replace("\r\n", "\n");
        foreach (var line in normalized.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            return new Uri(baseUri, line);
        }

        return null;
    }

    private static string TrimLiveMediaPlaylistForRecording(
        string playlist,
        DateTimeOffset nowUtc,
        ILogger<ProgramEndpointsMarker> logger,
        DateTimeOffset? recordingStartUtc)
    {
        var parseResult = ParseLiveMediaPlaylist(playlist);
        if (parseResult.IsMasterPlaylist || parseResult.Lines.Count == 0)
        {
            logger.ZLogDebug($"radiko live playlist trim skipped. reason=empty-or-master nowUtc={nowUtc:O} lineCount={parseResult.Lines.Count}");
            return playlist;
        }

        if (parseResult.FirstSegmentLineIndex < 0)
        {
            logger.ZLogDebug($"radiko live playlist trim skipped. reason=no-segment nowUtc={nowUtc:O} lineCount={parseResult.Lines.Count}");
            return playlist;
        }

        if (parseResult.SegmentBlocks.Count == 0)
        {
            logger.ZLogDebug($"radiko live playlist trim skipped. reason=no-block nowUtc={nowUtc:O} lineCount={parseResult.Lines.Count}");
            return parseResult.Normalized;
        }

        var segmentBlocks = parseResult.SegmentBlocks;
        var keepStartIndex = Math.Max(0, segmentBlocks.Count - LivePlaylistFallbackSegmentCount);
        var threshold = nowUtc - LivePlaylistProgramDateTimeLookback;
        var thresholdIndex = segmentBlocks.FindIndex(block =>
            block.EndTimeUtc is { } endTimeUtc && endTimeUtc >= threshold);
        var firstOriginalBlock = segmentBlocks.FirstOrDefault();
        var lastOriginalBlock = segmentBlocks.LastOrDefault();
        var trimMode = thresholdIndex >= 0 ? "program-date-time" : "fallback-tail";

        if (recordingStartUtc.HasValue)
        {
            var tailStartIndex = Math.Max(0, segmentBlocks.Count - LivePlaylistLiveEdgeSegmentCount);
            var recordingStartIndex = segmentBlocks.FindIndex(block =>
                block.EndTimeUtc is { } endTimeUtc && endTimeUtc >= recordingStartUtc.Value);
            keepStartIndex = recordingStartIndex >= 0
                ? Math.Max(tailStartIndex, recordingStartIndex)
                : tailStartIndex;
            trimMode = recordingStartIndex >= 0
                ? "recording-start-live-edge"
                : "live-edge-tail";
        }
        else if (thresholdIndex >= 0)
        {
            keepStartIndex = thresholdIndex;
        }

        if (keepStartIndex <= 0)
        {
            logger.ZLogDebug(
                $"radiko live playlist trim result. mode=keep-all thresholdUtc={threshold:O} recordingStartUtc={recordingStartUtc:O} nowUtc={nowUtc:O} originalSegments={segmentBlocks.Count} thresholdIndex={thresholdIndex} keepStartIndex={keepStartIndex} firstOriginalPdt={firstOriginalBlock?.ProgramDateTimeUtc:O} firstOriginalEndUtc={firstOriginalBlock?.EndTimeUtc:O} firstOriginalSegment={firstOriginalBlock?.SegmentUri} lastOriginalPdt={lastOriginalBlock?.ProgramDateTimeUtc:O} lastOriginalEndUtc={lastOriginalBlock?.EndTimeUtc:O} lastOriginalSegment={lastOriginalBlock?.SegmentUri}");
            return string.Join('\n', parseResult.HeaderLines
                .Concat(segmentBlocks.SelectMany(block => block.Lines))
                .Concat(parseResult.FooterLines));
        }

        var keptBlocks = segmentBlocks.Skip(keepStartIndex).ToList();
        var headerLines = parseResult.HeaderLines.ToList();
        var mediaSequenceIndex = headerLines.FindIndex(line =>
            line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.Ordinal));
        if (mediaSequenceIndex >= 0 &&
            int.TryParse(headerLines[mediaSequenceIndex]["#EXT-X-MEDIA-SEQUENCE:".Length..], out var mediaSequence))
        {
            headerLines[mediaSequenceIndex] = $"#EXT-X-MEDIA-SEQUENCE:{mediaSequence + keepStartIndex}";
        }

        logger.ZLogDebug(
            $"radiko live playlist trim result. mode={trimMode} thresholdUtc={threshold:O} recordingStartUtc={recordingStartUtc:O} nowUtc={nowUtc:O} originalSegments={segmentBlocks.Count} keptSegments={keptBlocks.Count} thresholdIndex={thresholdIndex} keepStartIndex={keepStartIndex} firstOriginalPdt={firstOriginalBlock?.ProgramDateTimeUtc:O} firstOriginalEndUtc={firstOriginalBlock?.EndTimeUtc:O} firstOriginalSegment={firstOriginalBlock?.SegmentUri} lastOriginalPdt={lastOriginalBlock?.ProgramDateTimeUtc:O} lastOriginalEndUtc={lastOriginalBlock?.EndTimeUtc:O} lastOriginalSegment={lastOriginalBlock?.SegmentUri} firstKeptPdt={keptBlocks.FirstOrDefault()?.ProgramDateTimeUtc:O} firstKeptEndUtc={keptBlocks.FirstOrDefault()?.EndTimeUtc:O} firstKeptSegment={keptBlocks.FirstOrDefault()?.SegmentUri} lastKeptPdt={keptBlocks.LastOrDefault()?.ProgramDateTimeUtc:O} lastKeptEndUtc={keptBlocks.LastOrDefault()?.EndTimeUtc:O} lastKeptSegment={keptBlocks.LastOrDefault()?.SegmentUri}");

        var rewrittenLines = new List<string>(headerLines.Count + keptBlocks.Sum(x => x.Lines.Count) + parseResult.FooterLines.Count);
        rewrittenLines.AddRange(headerLines);
        foreach (var block in keptBlocks)
        {
            rewrittenLines.AddRange(block.Lines);
        }

        rewrittenLines.AddRange(parseResult.FooterLines);
        return string.Join('\n', rewrittenLines);
    }

    private static LivePlaylistParseResult ParseLiveMediaPlaylist(string playlist)
    {
        var normalized = playlist.Replace("\r\n", "\n");
        var lines = normalized
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var isMasterPlaylist = lines.Any(line => line.StartsWith("#EXT-X-STREAM-INF", StringComparison.Ordinal));
        var firstSegmentLineIndex = lines.FindIndex(line => !line.StartsWith('#'));
        if (isMasterPlaylist || firstSegmentLineIndex < 0)
        {
            return new LivePlaylistParseResult(
                normalized,
                lines,
                isMasterPlaylist,
                firstSegmentLineIndex,
                [],
                [],
                []);
        }

        var headerLines = lines
            .Take(firstSegmentLineIndex)
            .Where(line => !line.StartsWith("#EXT-X-START:", StringComparison.Ordinal))
            .ToList();

        var footerLines = new List<string>();
        var segmentBlocks = new List<LivePlaylistSegmentBlock>();
        var currentLines = new List<string>();
        DateTimeOffset? currentProgramDateTime = null;
        double? currentDurationSeconds = null;

        for (var i = firstSegmentLineIndex; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith('#'))
            {
                if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.Ordinal))
                {
                    footerLines.Add(line);
                    continue;
                }

                currentLines.Add(line);
                if (line.StartsWith("#EXT-X-PROGRAM-DATE-TIME:", StringComparison.Ordinal))
                {
                    var value = line["#EXT-X-PROGRAM-DATE-TIME:".Length..];
                    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    {
                        currentProgramDateTime = parsed;
                    }
                }
                else if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
                {
                    var value = line["#EXTINF:".Length..];
                    var commaIndex = value.IndexOf(',');
                    if (commaIndex >= 0)
                    {
                        value = value[..commaIndex];
                    }

                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var durationSeconds))
                    {
                        currentDurationSeconds = durationSeconds;
                    }
                }

                continue;
            }

            currentLines.Add(line);
            segmentBlocks.Add(new LivePlaylistSegmentBlock(
                currentLines.ToList(),
                currentProgramDateTime,
                currentDurationSeconds));
            currentLines.Clear();
            currentProgramDateTime = null;
            currentDurationSeconds = null;
        }

        return new LivePlaylistParseResult(
            normalized,
            lines,
            isMasterPlaylist,
            firstSegmentLineIndex,
            headerLines,
            footerLines,
            segmentBlocks);
    }

    private sealed record LivePlaylistSegmentBlock(
        List<string> Lines,
        DateTimeOffset? ProgramDateTimeUtc,
        double? DurationSeconds)
    {
        public string? SegmentUri => Lines.LastOrDefault(line => !line.StartsWith('#'));

        public DateTimeOffset? EndTimeUtc =>
            ProgramDateTimeUtc.HasValue && DurationSeconds.HasValue
                ? ProgramDateTimeUtc.Value.AddSeconds(DurationSeconds.Value)
                : null;
    }

    private sealed record LivePlaylistParseResult(
        string Normalized,
        List<string> Lines,
        bool IsMasterPlaylist,
        int FirstSegmentLineIndex,
        List<string> HeaderLines,
        List<string> FooterLines,
        List<LivePlaylistSegmentBlock> SegmentBlocks);
}



