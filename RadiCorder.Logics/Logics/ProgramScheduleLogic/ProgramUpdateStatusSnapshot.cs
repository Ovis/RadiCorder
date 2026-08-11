namespace RadiCorder.Logics.Logics.ProgramScheduleLogic;

/// <summary>
/// 番組表更新状態のスナップショット。
/// </summary>
/// <param name="IsRunning">更新処理実行中かどうか</param>
/// <param name="TriggerSource">起動元</param>
/// <param name="Message">状態メッセージ</param>
/// <param name="StartedAtUtc">開始時刻(UTC)</param>
/// <param name="LastCompletedAtUtc">最終完了時刻(UTC)</param>
/// <param name="LastSucceeded">最終結果が成功かどうか</param>
public sealed record ProgramUpdateStatusSnapshot(
    bool IsRunning,
    string? TriggerSource,
    string Message,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    bool? LastSucceeded);
