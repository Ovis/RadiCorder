namespace RadiCorder.Logics.Domain.Notification;

/// <summary>
/// お知らせ更新イベントを外部へ通知する。
/// </summary>
public interface INotificationEventPublisher
{
    /// <summary>
    /// お知らせ更新イベントを発行する。
    /// </summary>
    /// <param name="payload">通知ペイロード</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask PublishAsync(NotificationChangedEvent payload, CancellationToken cancellationToken = default);
}
