using RadiCorder.Logics.Domain.Recording;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// インメモリの録音リポジトリ
/// </summary>
public class InMemoryRecordingRepository : IRecordingRepository
{
    public readonly Dictionary<Ulid, (ProgramRecordingInfo Metadata, MediaPath Path, RecordingOptions Options, RecordingState State, string? Error)> Store = new();

    /// <summary>
    /// 録音レコードを作成する
    /// </summary>
    public ValueTask<Ulid> CreateAsync(ProgramRecordingInfo metadata, MediaPath path, RecordingOptions options, CancellationToken cancellationToken = default)
    {
        var id = Ulid.NewUlid();
        Store[id] = (metadata, path, options, RecordingState.Pending, null);
        return ValueTask.FromResult(id);
    }

    /// <summary>
    /// 録音状態を更新する
    /// </summary>
    public ValueTask UpdateStateAsync(Ulid recordingId, RecordingState state, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        if (Store.TryGetValue(recordingId, out var entry))
        {
            Store[recordingId] = (entry.Metadata, entry.Path, entry.Options, state, errorMessage);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 録音ファイルパスを更新する
    /// </summary>
    public ValueTask UpdateFilePathAsync(Ulid recordingId, MediaPath path, CancellationToken cancellationToken = default)
    {
        if (Store.TryGetValue(recordingId, out var entry))
        {
            Store[recordingId] = (entry.Metadata, path, entry.Options, entry.State, entry.Error);
        }
        return ValueTask.CompletedTask;
    }
}
