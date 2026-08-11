using Microsoft.AspNetCore.SignalR;
using RadiCorder.Logics.Domain.Notification;
using ZLogger;

namespace RadiCorder.Hubs;

/// <summary>
/// お知らせ更新イベントを SignalR で配信する実装。
/// </summary>
public class NotificationSignalRPublisher(
    ILogger<NotificationSignalRPublisher> logger,
    IHubContext<NotificationHub> hubContext) : INotificationEventPublisher
{
    /// <summary>
    /// お知らせ更新イベントを全クライアントに配信する。
    /// </summary>
    /// <param name="payload">通知ペイロード</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public async ValueTask PublishAsync(NotificationChangedEvent payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(NotificationHubMethods.NotificationChanged, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"お知らせ更新イベントの配信に失敗しました。");
        }
    }
}
