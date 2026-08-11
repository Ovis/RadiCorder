namespace RadiCorder.Logics.Domain.Reserve;

/// <summary>
/// 録音予定一覧の更新イベントを外部へ通知する。
/// </summary>
public interface IReserveScheduleEventPublisher
{
    /// <summary>
    /// 録音予定一覧の更新イベントを発行する。
    /// </summary>
    /// <param name="payload">通知ペイロード</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask PublishAsync(ReserveScheduleChangedEvent payload, CancellationToken cancellationToken = default);
}
