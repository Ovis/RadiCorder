using System.Threading.Channels;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 手動の類似録音抽出要求をホストが管理するWorkerへ渡す。
/// </summary>
public class DuplicateDetectionQueue
{
    public sealed record Request(int LookbackDays, int MaxPhase1Groups, string Phase2Mode, int BroadcastClusterWindowHours);
    private readonly Channel<Request> _requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true
    });

    public bool TryEnqueue(Request request) => _requests.Writer.TryWrite(request);
    public IAsyncEnumerable<Request> ReadAllAsync(CancellationToken cancellationToken) => _requests.Reader.ReadAllAsync(cancellationToken);
}
