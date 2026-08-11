namespace RadiCorder.Logics.Domain.Reserve;

/// <summary>
/// 録音予定一覧更新時に配信するイベント情報。
/// </summary>
/// <param name="UpdatedAtUtc">更新時刻(UTC)</param>
public sealed record ReserveScheduleChangedEvent(DateTimeOffset UpdatedAtUtc);
