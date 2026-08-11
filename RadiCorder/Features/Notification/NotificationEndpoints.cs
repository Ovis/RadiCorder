using Microsoft.AspNetCore.Http.HttpResults;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Models;

namespace RadiCorder.Features.Notification;

/// <summary>
/// お知らせ関連の Api エンドポイントを提供する。
/// </summary>
public static class NotificationEndpoints
{
    /// <summary>
    /// お知らせ関連エンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications").WithTags("NotificationsApi");
        group.MapGet("/count", HandleUnreadCountAsync)
            .WithName("ApiNotificationCount")
            .WithSummary("未読のお知らせ件数を取得する");
        group.MapGet("/latest", HandleUnreadLatestAsync)
            .WithName("ApiNotificationLatest")
            .WithSummary("未読のお知らせ一覧を取得する");
        group.MapGet("/", HandleListAsync)
            .WithName("ApiNotificationList")
            .WithSummary("お知らせ一覧を取得する");
        group.MapPost("/clear", HandleClearAsync)
            .WithName("ApiNotificationClear")
            .WithSummary("お知らせを全削除する");
        return endpoints;
    }

    /// <summary>
    /// 未読のお知らせ件数を取得する。
    /// </summary>
    private static async Task<Ok<ApiResponse<int>>> HandleUnreadCountAsync(
        NotificationLobLogic notificationLobLogic)
    {
        var count = await notificationLobLogic.GetUnreadNotificationCountAsync();
        return TypedResults.Ok(ApiResponse.Ok(count));
    }

    /// <summary>
    /// 未読のお知らせ一覧を取得する。
    /// </summary>
    private static async Task<Ok<ApiResponse<NotificationLatestResponse>>> HandleUnreadLatestAsync(
        NotificationLobLogic notificationLobLogic)
    {
        var list = await notificationLobLogic.GetUnreadNotificationListAsync();
        var data = new NotificationLatestResponse(list.Count, list.Take(5).ToList());
        return TypedResults.Ok(ApiResponse.Ok(data));
    }

    /// <summary>
    /// お知らせ一覧を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<NotificationListResponse>>, BadRequest<ApiResponse<EmptyData?>>>> HandleListAsync(
        NotificationLobLogic notificationLobLogic,
        int page = 1,
        int pageSize = 20)
    {
        var (isSuccess, total, list, _) = await notificationLobLogic.GetNotificationListAsync(page, pageSize);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("お知らせの取得に失敗しました。"));
        }

        var data = new NotificationListResponse(total, page, pageSize, list ?? []);
        return TypedResults.Ok(ApiResponse.Ok(data));
    }

    /// <summary>
    /// お知らせを全削除する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleClearAsync(
        NotificationLobLogic notificationLobLogic)
    {
        await notificationLobLogic.DeleteAllNotificationAsync();
        return TypedResults.Ok(ApiResponse.Ok("削除しました。"));
    }

    /// <summary>
    /// 未読通知簡易一覧レスポンス。
    /// </summary>
    private sealed record NotificationLatestResponse(int Count, List<NotificationEntry> List);

    /// <summary>
    /// 通知一覧レスポンス。
    /// </summary>
    private sealed record NotificationListResponse(int TotalRecords, int Page, int PageSize, List<NotificationEntry> Recordings);
}



