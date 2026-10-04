using System.Globalization;
using RadiCorder.Logics.BackgroundServices;
using Microsoft.AspNetCore.Http.HttpResults;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Logics.PlayProgramLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Features.Program;

/// <summary>
/// 番組関連の Api エンドポイントを提供する。
/// </summary>
public static class ProgramEndpoints
{
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
        group.MapRadikoStreamingEndpoints();
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
        ProgramSearchService programSearchService,
        ProgramSearchEntity entity)
    {
        var result = await programSearchService.SearchAsync(entity);
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
    private static Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateProgramsAsync(
        ProgramUpdateQueue programUpdateQueue)
    {
        programUpdateQueue.Enqueue("manual");
        return Task.FromResult<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>>(
            TypedResults.Ok(ApiResponse.Ok("番組表更新処理を実行中です。通常数分で更新が完了します。")));
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
    /// ProgramEndpoints 用のロガーカテゴリ型。
    /// </summary>
    internal sealed class ProgramEndpointsMarker;

    /// <summary>
    /// エリア一覧表示用の内部モデル。
    /// </summary>
    private sealed record ProgramAreaEntry(string AreaId, string AreaName, int AreaOrder, int ServiceOrder);




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












}



