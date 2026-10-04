namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 共通のライブ・アーカイブ取得と異なる方式を登録する。
/// </summary>
public interface IRecordingAcquisitionMethod
{
    string Method { get; }
    ValueTask<bool> RecordAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken);
}
