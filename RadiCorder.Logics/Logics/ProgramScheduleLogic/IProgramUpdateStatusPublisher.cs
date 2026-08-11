namespace RadiCorder.Logics.Logics.ProgramScheduleLogic;

/// <summary>
/// 番組表更新状態の変更通知を配信する。
/// </summary>
public interface IProgramUpdateStatusPublisher
{
    /// <summary>
    /// 更新状態を配信する。
    /// </summary>
    /// <param name="status">更新状態</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask PublishAsync(ProgramUpdateStatusSnapshot status, CancellationToken cancellationToken = default);
}
