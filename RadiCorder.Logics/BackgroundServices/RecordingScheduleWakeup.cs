namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 録音スケジューラの待機解除を担うシグナル。
/// </summary>
public class RecordingScheduleWakeup : IRecordingScheduleWakeup
{
    private readonly object _lock = new();
    private TaskCompletionSource<bool> _signal = CreateSignal();

    /// <summary>
    /// 録音スケジューラへ即時再評価を要求する。
    /// </summary>
    public void Wake()
    {
        lock (_lock)
        {
            _signal.TrySetResult(true);
        }
    }

    /// <summary>
    /// 録音スケジューラの起床通知を待機する。
    /// </summary>
    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        Task waitTask;

        lock (_lock)
        {
            waitTask = _signal.Task;
        }

        await waitTask.WaitAsync(cancellationToken);

        lock (_lock)
        {
            if (_signal.Task.IsCompleted)
            {
                _signal = CreateSignal();
            }
        }
    }

    private static TaskCompletionSource<bool> CreateSignal()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
