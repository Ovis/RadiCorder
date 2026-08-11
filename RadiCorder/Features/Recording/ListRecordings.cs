using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Models;

namespace RadiCorder.Features.Recording;

/// <summary>
/// 録音済み一覧系の Api エンドポイントを提供する。
/// </summary>
public static class ListRecordings
{
    /// <summary>
    /// 録音済み一覧関連のエンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapRecordingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/recordings").WithTags("RecordingsApi");
        group.MapRecordingMutationEndpoints();
        group.MapRecordingPlaybackEndpoints();
        group.MapGet("/", HandleAsync)
            .WithName("ApiListRecordings")
            .WithSummary("録音済み一覧を取得する");
        group.MapGet("/stations", HandleStationsAsync)
            .WithName("ApiListRecordingStations")
            .WithSummary("録音済み一覧の放送局フィルタ候補を取得する");
        group.MapGet("/duplicates/status", HandleDuplicateStatusAsync)
            .WithName("ApiRecordingDuplicateStatus")
            .WithSummary("類似録音抽出ジョブの状態を取得する");
        group.MapGet("/duplicates/candidates", HandleDuplicateCandidatesAsync)
            .WithName("ApiRecordingDuplicateCandidates")
            .WithSummary("直近の類似録音抽出結果を取得する");
        group.MapPost("/duplicates/run", HandleDuplicateRunAsync)
            .WithName("ApiRunRecordingDuplicateDetection")
            .WithSummary("類似録音抽出ジョブを即時実行する");
        return endpoints;
    }

    private static async Task<Results<Ok<ApiResponse<ListRecordingsResponse>>, BadRequest<ApiResponse<EmptyData?>>>> HandleAsync(
        [AsParameters] ListRecordingsQuery query,
        [FromServices] RecordedRadioLobLogic recordedRadioLobLogic)
    {
        // クエリ文字列の tagIds を安全に Guid リストへ正規化する。
        var parsedTagIds = (query.TagIds ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Guid.TryParse(x, out var id) ? id : (Guid?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        var (isSuccess, total, list, _) = await recordedRadioLobLogic.GetRecorderProgramListAsync(
            query.SearchQuery ?? string.Empty,
            query.Page,
            query.PageSize,
            query.SortBy ?? "StartDateTime",
            query.IsDescending,
            query.WithinDays,
            query.StationId ?? string.Empty,
            parsedTagIds,
            query.TagMode ?? "or",
            query.UntaggedOnly ?? false,
            query.UnlistenedOnly ?? false);

        if (!isSuccess || list == null)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("録音済み番組の取得に失敗しました。"));
        }

        var response = new ListRecordingsResponse(
            total,
            query.Page,
            query.PageSize,
            list);

        return TypedResults.Ok(ApiResponse.Ok(response));
    }

    /// <summary>
    /// 録音済み一覧で使用する放送局フィルタ候補を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<List<RecordedStationFilterEntry>>>, BadRequest<ApiResponse<EmptyData?>>>> HandleStationsAsync(
        [FromServices] RecordedRadioLobLogic recordedRadioLobLogic)
    {
        var (isSuccess, list, _) = await recordedRadioLobLogic.GetRecordedStationFiltersAsync();
        if (!isSuccess || list == null)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("放送局フィルタ候補の取得に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok(list));
    }

    /// <summary>
    /// 類似録音抽出ジョブの現在状態を取得する。
    /// </summary>
    private static Ok<ApiResponse<RecordedDuplicateDetectionStatusEntry>> HandleDuplicateStatusAsync(
        [FromServices] RecordedDuplicateDetectionLobLogic duplicateDetectionLobLogic)
    {
        return TypedResults.Ok(ApiResponse.Ok(duplicateDetectionLobLogic.GetStatus()));
    }

    /// <summary>
    /// 直近の類似録音抽出結果を取得する。
    /// </summary>
    private static Ok<ApiResponse<List<RecordedDuplicateCandidateEntry>>> HandleDuplicateCandidatesAsync(
        [FromServices] RecordedDuplicateDetectionLobLogic duplicateDetectionLobLogic)
    {
        return TypedResults.Ok(ApiResponse.Ok(duplicateDetectionLobLogic.GetLastCandidates()));
    }

    /// <summary>
    /// 類似録音抽出ジョブを即時実行する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleDuplicateRunAsync(
        [FromBody] DuplicateDetectionRunRequest? request,
        [FromServices] RecordedDuplicateDetectionLobLogic duplicateDetectionLobLogic)
    {
        var lookbackDays = request?.LookbackDays ?? 30;
        var maxPhase1Groups = request?.MaxPhase1Groups ?? 100;
        var phase2Mode = request?.Phase2Mode ?? "light";
        var broadcastClusterWindowHours = request?.BroadcastClusterWindowHours ?? 48;

        var (isSuccess, message, _) = await duplicateDetectionLobLogic.StartImmediateAsync(
            lookbackDays,
            maxPhase1Groups,
            phase2Mode,
            broadcastClusterWindowHours);

        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(message));
        }

        return TypedResults.Ok(ApiResponse.Ok(message));
    }
}

/// <summary>
/// 録音済み一覧取得クエリ。
/// </summary>
public sealed record ListRecordingsQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? SearchQuery { get; init; }
    public string? SortBy { get; init; } = "StartDateTime";
    public bool IsDescending { get; init; } = true;
    public int? WithinDays { get; init; }
    public string? StationId { get; init; }
    public string? TagIds { get; init; }
    public string? TagMode { get; init; } = "or";
    public bool? UntaggedOnly { get; init; }
    public bool? UnlistenedOnly { get; init; }
}

/// <summary>
/// 録音済み一覧レスポンス。
/// </summary>
public sealed record ListRecordingsResponse(
    int TotalRecords,
    int Page,
    int PageSize,
    List<RecordedProgramEntry> Recordings);



