namespace RadiCorder.Logics.Domain.AppEvent;

/// <summary>
/// 全画面通知向けトーストイベント情報。
/// </summary>
/// <param name="Message">表示メッセージ</param>
/// <param name="IsSuccess">成功トーストかどうか</param>
/// <param name="OccurredAtUtc">発生時刻(UTC)</param>
public sealed record AppToastEvent(
    string Message,
    bool IsSuccess,
    DateTimeOffset OccurredAtUtc);
