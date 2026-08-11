namespace RadiCorder.Logics.Domain.AppEvent;

/// <summary>
/// 機能別の処理完了/失敗を通知するイベント情報。
/// </summary>
/// <param name="Category">機能カテゴリ</param>
/// <param name="Action">実行アクション</param>
/// <param name="Succeeded">成功可否</param>
/// <param name="Message">表示メッセージ</param>
/// <param name="OccurredAtUtc">発生時刻(UTC)</param>
public sealed record AppOperationEvent(
    string Category,
    string Action,
    bool Succeeded,
    string Message,
    DateTimeOffset OccurredAtUtc);
