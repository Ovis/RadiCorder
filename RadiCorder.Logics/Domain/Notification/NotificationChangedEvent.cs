namespace RadiCorder.Logics.Domain.Notification;

/// <summary>
/// お知らせ更新時に配信するイベント情報。
/// </summary>
/// <param name="UpdatedAtUtc">更新時刻(UTC)</param>
public sealed record NotificationChangedEvent(DateTimeOffset UpdatedAtUtc);
