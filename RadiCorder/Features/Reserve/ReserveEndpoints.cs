using Microsoft.AspNetCore.Http.HttpResults;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Features.Reserve;

/// <summary>
/// 予約関連の Api エンドポイントを提供する。
/// </summary>
public static class ReserveEndpoints
{
    /// <summary>
    /// 予約関連エンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapReserveEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reserves").WithTags("ReservesApi");
        group.MapGet("/keywords", HandleGetKeywordReserveListAsync)
            .WithName("ApiReserveKeywords")
            .WithSummary("キーワード予約一覧を取得する");
        group.MapPost("/keywords/update", HandleUpdateKeywordReserveEntryAsync)
            .WithName("ApiReserveKeywordUpdate")
            .WithSummary("キーワード予約を更新する");
        group.MapPost("/keywords/delete", HandleDeleteKeywordReserveEntryAsync)
            .WithName("ApiReserveKeywordDelete")
            .WithSummary("キーワード予約を削除する");
        group.MapPost("/keywords/switch", HandleSwitchKeywordReserveEntryStatusAsync)
            .WithName("ApiReserveKeywordSwitch")
            .WithSummary("キーワード予約の有効状態を切り替える");
        group.MapPost("/keywords/reorder", HandleReorderKeywordReservesAsync)
            .WithName("ApiReserveKeywordReorder")
            .WithSummary("キーワード予約の並び順を更新する");
        group.MapGet("/programs", HandleGetReserveListAsync)
            .WithName("ApiReservePrograms")
            .WithSummary("番組予約一覧を取得する");
        group.MapPost("/programs/delete", HandleDeleteProgramReserveEntryAsync)
            .WithName("ApiReserveProgramDelete")
            .WithSummary("番組予約を削除する");
        return endpoints;
    }

    /// <summary>
    /// キーワード予約一覧を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<List<KeywordReserveEntry>>>, BadRequest<ApiResponse<EmptyData?>>>> HandleGetKeywordReserveListAsync(
        ReserveLobLogic reserveLobLogic)
    {
        var (isSuccess, entry, error) = await reserveLobLogic.GetKeywordReserveListAsync();
        if (!isSuccess || entry == null)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "キーワード予約の取得に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok(entry));
    }

    /// <summary>
    /// キーワード予約を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateKeywordReserveEntryAsync(
        KeywordReserveEntry entry,
        ReserveLobLogic reserveLobLogic)
    {
        if (!Enum.IsDefined(typeof(KeywordReserveTagMergeBehavior), entry.MergeTagBehavior))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("タグマージ設定が不正です。"));
        }

        var (isSuccess, error) = await reserveLobLogic.UpdateKeywordReserveAsync(entry);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "更新に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// キーワード予約を削除する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleDeleteKeywordReserveEntryAsync(
        ReserveEntryRequest request,
        ReserveLobLogic reserveLobLogic)
    {
        if (!TryParseReserveId(request.Id, out var reserveId))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("Invalid reserve id."));
        }

        var (isSuccess, error) = await reserveLobLogic.DeleteKeywordReserveAsync(reserveId);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "削除に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("削除しました。"));
    }

    /// <summary>
    /// キーワード予約の有効状態を切り替える。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleSwitchKeywordReserveEntryStatusAsync(
        ReserveEntryRequest request,
        ReserveLobLogic reserveLobLogic)
    {
        if (!TryParseReserveId(request.Id, out var reserveId))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("Invalid reserve id."));
        }

        var (isSuccess, error) = await reserveLobLogic.SwitchKeywordReserveEntryStatusAsync(reserveId);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "更新に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// キーワード予約の並び順を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleReorderKeywordReservesAsync(
        KeywordReserveReorderRequest request,
        ReserveLobLogic reserveLobLogic)
    {
        if (request.Ids.Count == 0)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("並び替え対象が指定されていません。"));
        }

        var orderedIds = new List<Ulid>(request.Ids.Count);
        foreach (var id in request.Ids)
        {
            if (!TryParseReserveId(id, out var reserveId))
            {
                return TypedResults.BadRequest(ApiResponse.Fail("Invalid reserve id."));
            }

            orderedIds.Add(reserveId);
        }

        var (isSuccess, error) = await reserveLobLogic.ReorderKeywordReservesAsync(orderedIds);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "並び替えに失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("並び順を更新しました。"));
    }

    /// <summary>
    /// 番組予約一覧を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<List<ScheduleEntry>>>, BadRequest<ApiResponse<EmptyData?>>>> HandleGetReserveListAsync(
        ReserveLobLogic reserveLobLogic)
    {
        var (isSuccess, entry, error) = await reserveLobLogic.GetReserveListAsync();
        if (!isSuccess || entry == null)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "番組予約の取得に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok(entry));
    }

    /// <summary>
    /// 番組予約を削除する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleDeleteProgramReserveEntryAsync(
        ReserveEntryRequest request,
        ReserveLobLogic reserveLobLogic)
    {
        if (!TryParseReserveId(request.Id, out var reserveId))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("Invalid reserve id."));
        }

        var (isSuccess, error) = await reserveLobLogic.DeleteProgramReserveEntryAsync(reserveId);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(error?.Message ?? "削除に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("削除しました。"));
    }

    /// <summary>
    /// 予約ID文字列を ULID に変換する。
    /// </summary>
    private static bool TryParseReserveId(string? id, out Ulid reserveId)
    {
        reserveId = default;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return Ulid.TryParse(id, out reserveId);
    }
}



