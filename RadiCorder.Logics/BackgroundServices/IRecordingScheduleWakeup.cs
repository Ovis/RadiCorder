namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 録音スケジューラへ即時再評価を通知する。
/// </summary>
public interface IRecordingScheduleWakeup
{
    /// <summary>
    /// 録音スケジューラの待機を解除する。
    /// </summary>
    void Wake();

    /// <summary>
    /// 録音スケジューラの起床通知を待機する。
    /// </summary>
    ValueTask WaitAsync(CancellationToken cancellationToken);
}
