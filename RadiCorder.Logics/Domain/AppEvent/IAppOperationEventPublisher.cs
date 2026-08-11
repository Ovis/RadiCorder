namespace RadiCorder.Logics.Domain.AppEvent;

/// <summary>
/// 機能別の処理状態イベントを配信する。
/// </summary>
public interface IAppOperationEventPublisher
{
    /// <summary>
    /// 処理状態イベントを配信する。
    /// </summary>
    /// <param name="payload">配信内容</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask PublishAsync(AppOperationEvent payload, CancellationToken cancellationToken = default);
}
