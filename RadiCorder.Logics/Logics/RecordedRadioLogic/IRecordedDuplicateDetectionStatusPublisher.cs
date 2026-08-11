using RadiCorder.Logics.Models;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic;

/// <summary>
/// 同一番組候補チェック状態の変更通知を配信する。
/// </summary>
public interface IRecordedDuplicateDetectionStatusPublisher
{
    /// <summary>
    /// 同一番組候補チェック状態を配信する。
    /// </summary>
    /// <param name="status">現在状態</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask PublishAsync(RecordedDuplicateDetectionStatusEntry status, CancellationToken cancellationToken = default);
}
