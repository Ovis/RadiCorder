namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音状態変更イベントを外部へ通知する。
/// </summary>
public interface IRecordingStateEventPublisher
{
    /// <summary>
    /// 録音状態変更イベントを発行する。
    /// </summary>
    /// <param name="payload">通知ペイロード</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask PublishAsync(RecordingStateChangedEvent payload, CancellationToken cancellationToken = default);
}
