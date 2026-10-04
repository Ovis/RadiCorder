namespace RadiCorder.Logics.Logics.ProgramScheduleLogic;

/// <summary>
/// 番組表更新状態を保持するインメモリ実装。
/// </summary>
public class ProgramUpdateStatusService : IProgramUpdateStatusService
{
    private readonly object sync = new();
    private ProgramUpdateStatusSnapshot snapshot = new(
        IsRunning: false,
        TriggerSource: null,
        Message: "待機中",
        StartedAtUtc: null,
        LastCompletedAtUtc: null,
        LastSucceeded: null);

    /// <summary>
    /// 現在の更新状態を取得する。
    /// </summary>
    public ProgramUpdateStatusSnapshot GetCurrent()
    {
        lock (sync)
        {
            return snapshot;
        }
    }

    /// <summary>
    /// 更新開始状態を記録する。
    /// </summary>
    /// <param name="triggerSource">起動元</param>
    public ProgramUpdateStatusSnapshot MarkStarted(string triggerSource)
    {
        lock (sync)
        {
            snapshot = snapshot with
            {
                IsRunning = true,
                TriggerSource = triggerSource,
                Message = "番組表更新を実行中です。",
                StartedAtUtc = DateTimeOffset.UtcNow
            };
            return snapshot;
        }
    }

    /// <summary>
    /// 更新成功状態を記録する。
    /// </summary>
    public ProgramUpdateStatusSnapshot MarkSucceeded()
    {
        lock (sync)
        {
            snapshot = snapshot with
            {
                IsRunning = false,
                Message = "番組表更新が完了しました。",
                LastCompletedAtUtc = DateTimeOffset.UtcNow,
                LastSucceeded = true
            };
            return snapshot;
        }
    }

    /// <summary>
    /// 更新失敗状態を記録する。
    /// </summary>
    public ProgramUpdateStatusSnapshot MarkFailed()
        => MarkFailed("番組表更新に失敗しました。");

    public ProgramUpdateStatusSnapshot MarkFailed(string message)
    {
        lock (sync)
        {
            snapshot = snapshot with
            {
                IsRunning = false,
                Message = message,
                LastCompletedAtUtc = DateTimeOffset.UtcNow,
                LastSucceeded = false
            };
            return snapshot;
        }
    }
}
