namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音ソースの一時的な失敗時に再試行前の調整を行う。
/// </summary>
public interface IRecordingSourceRetryHandler
{
    /// <summary>
    /// 再試行前の状態調整を実行する。
    /// </summary>
    ValueTask PrepareForRetryAsync(RecordingCommand command, CancellationToken cancellationToken = default);
}
