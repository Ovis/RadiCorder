namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 保存済み録音を、DB障害や再起動後に確定できるストレージ。
/// </summary>
public interface IRecoverableMediaStorageService : IMediaStorageService
{
    ValueTask<MediaPath> CommitRecordingAsync(MediaPath path, Ulid recordingId, string? scheduleJobId, CancellationToken cancellationToken);
    void CompleteFinalization(Ulid recordingId);
    void CompleteJobFinalization(Ulid scheduleJobId);
}
