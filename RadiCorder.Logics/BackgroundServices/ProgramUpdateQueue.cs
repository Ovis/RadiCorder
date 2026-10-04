using System.Threading.Channels;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// Webと起動処理からの更新要求を、ホストが管理するWorkerへ渡す。
/// </summary>
public class ProgramUpdateQueue
{
    private readonly Channel<string> requests = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true
    });

    public void Enqueue(string source) => requests.Writer.TryWrite(source);
    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) => requests.Reader.ReadAllAsync(cancellationToken);
}
