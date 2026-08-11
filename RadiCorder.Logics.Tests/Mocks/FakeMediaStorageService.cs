using RadiCorder.Logics.Domain.Recording;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// 保存処理のテスト用スタブ
/// </summary>
public class FakeMediaStorageService : IMediaStorageService
{
    public MediaPath Prepared { get; private set; } = new("temp.m4a", "final.m4a", "final.m4a");

    /// <summary>
    /// CommitAsyncが呼ばれたかどうか
    /// </summary>
    public bool IsCommitCalled { get; private set; }

    /// <summary>
    /// CleanupTempAsyncが呼ばれたかどうか
    /// </summary>
    public bool IsCleanupCalled { get; private set; }

    /// <summary>
    /// 事前に固定したパスを返す
    /// </summary>
    public ValueTask<MediaPath> PrepareAsync(ProgramRecordingInfo metadata, RecordingOptions options, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Prepared);
    }

    /// <summary>
    /// コミット処理のスタブ
    /// </summary>
    public ValueTask<MediaPath> CommitAsync(MediaPath path, CancellationToken cancellationToken = default)
    {
        IsCommitCalled = true;
        return ValueTask.FromResult(path);
    }

    /// <summary>
    /// 一時ファイル削除のスタブ
    /// </summary>
    public ValueTask CleanupTempAsync(MediaPath path, CancellationToken cancellationToken = default)
    {
        IsCleanupCalled = true;
        return ValueTask.CompletedTask;
    }

    public ValueTask<SaveFailedFallbackResult> SaveFailedAsync(
        MediaPath path,
        SaveFailedFallbackMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new SaveFailedFallbackResult("save-failed\\fallback.m4a", "save-failed\\fallback.m4a.meta.json"));
    }
}
