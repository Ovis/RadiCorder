namespace RadiCorder.Logics.Domain.AppEvent;

/// <summary>
/// 全画面通知向けトーストイベントを配信する。
/// </summary>
public interface IAppToastEventPublisher
{
    /// <summary>
    /// トーストイベントを配信する。
    /// </summary>
    /// <param name="payload">配信内容</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask PublishAsync(AppToastEvent payload, CancellationToken cancellationToken = default);
}
