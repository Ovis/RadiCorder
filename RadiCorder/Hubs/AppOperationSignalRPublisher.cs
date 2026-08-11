using Microsoft.AspNetCore.SignalR;
using RadiCorder.Logics.Domain.AppEvent;
using ZLogger;

namespace RadiCorder.Hubs;

/// <summary>
/// 機能別処理イベントを SignalR で配信する実装。
/// </summary>
public class AppOperationSignalRPublisher(
    ILogger<AppOperationSignalRPublisher> logger,
    IHubContext<AppEventHub> hubContext) : IAppOperationEventPublisher
{
    /// <summary>
    /// 処理イベントを全クライアントに配信する。
    /// </summary>
    /// <param name="payload">配信内容</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public async ValueTask PublishAsync(AppOperationEvent payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(AppEventHubMethods.Operation, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"機能別処理イベントの配信に失敗しました。");
        }
    }
}
