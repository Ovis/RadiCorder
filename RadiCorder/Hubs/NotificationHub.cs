using Microsoft.AspNetCore.SignalR;

namespace RadiCorder.Hubs;

/// <summary>
/// お知らせイベント配信用の SignalR Hub。
/// </summary>
public class NotificationHub : Hub
{
}

/// <summary>
/// お知らせHubの送信メソッド名定義。
/// </summary>
public static class NotificationHubMethods
{
    /// <summary>
    /// お知らせ更新通知イベント名。
    /// </summary>
    public const string NotificationChanged = "notificationChanged";
}
