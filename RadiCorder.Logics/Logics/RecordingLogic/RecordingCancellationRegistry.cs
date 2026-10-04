using System.Collections.Concurrent;

namespace RadiCorder.Logics.Logics.RecordingLogic
{
    /// <summary>
    /// 実行中録音のキャンセルトークンを管理する
    /// </summary>
    public static class RecordingCancellationRegistry
    {
        private static readonly ConcurrentDictionary<string, Registration> TokenMap = new();

        public static void Register(string scheduleJobId, CancellationTokenSource cts)
        {
            if (string.IsNullOrWhiteSpace(scheduleJobId))
            {
                return;
            }

            var registration = new Registration(cts);
            TokenMap.AddOrUpdate(scheduleJobId, registration, (_, previous) => { previous.Complete(); return registration; });
        }

        public static void Unregister(string scheduleJobId)
        {
            if (string.IsNullOrWhiteSpace(scheduleJobId))
            {
                return;
            }

            if (TokenMap.TryRemove(scheduleJobId, out var registration)) registration.Complete();
        }

        public static bool Cancel(string scheduleJobId)
        {
            if (string.IsNullOrWhiteSpace(scheduleJobId))
            {
                return false;
            }

            if (TokenMap.TryGetValue(scheduleJobId, out var registration))
            {
                return registration.Cancel();
            }

            return false;
        }

        /// <summary>
        /// 再有効化が終了状態の保存と競合しないよう、実行中処理の終了まで待つ。
        /// </summary>
        public static async ValueTask CancelAndWaitAsync(string scheduleJobId, CancellationToken cancellationToken = default)
        {
            if (!TokenMap.TryGetValue(scheduleJobId, out var registration)) return;
            registration.Cancel();
            await registration.Completion.Task.WaitAsync(cancellationToken);
        }

        private sealed class Registration(CancellationTokenSource source)
        {
            private readonly object _lock = new();
            private bool _completed;
            public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Cancel()
            {
                lock (_lock)
                {
                    if (_completed) return false;
                    source.Cancel();
                    return true;
                }
            }
            public void Complete()
            {
                lock (_lock) { _completed = true; Completion.TrySetResult(); }
            }
        }
    }
}
