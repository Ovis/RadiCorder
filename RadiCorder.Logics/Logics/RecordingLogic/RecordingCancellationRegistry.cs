using System.Collections.Concurrent;

namespace RadiCorder.Logics.Logics.RecordingLogic
{
    /// <summary>
    /// 実行中録音のキャンセルトークンを管理する
    /// </summary>
    public static class RecordingCancellationRegistry
    {
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> TokenMap = new();

        public static void Register(string scheduleJobId, CancellationTokenSource cts)
        {
            if (string.IsNullOrWhiteSpace(scheduleJobId))
            {
                return;
            }

            TokenMap.AddOrUpdate(scheduleJobId, cts, (_, _) => cts);
        }

        public static void Unregister(string scheduleJobId)
        {
            if (string.IsNullOrWhiteSpace(scheduleJobId))
            {
                return;
            }

            TokenMap.TryRemove(scheduleJobId, out _);
        }

        public static bool Cancel(string scheduleJobId)
        {
            if (string.IsNullOrWhiteSpace(scheduleJobId))
            {
                return false;
            }

            if (TokenMap.TryGetValue(scheduleJobId, out var cts))
            {
                cts.Cancel();
                return true;
            }

            return false;
        }
    }
}
