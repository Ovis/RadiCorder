using Microsoft.AspNetCore.SignalR;
using RadiCorder.Logics.Domain.AppEvent;
using ZLogger;

namespace RadiCorder.Hubs;

/// <summary>
/// 全画面向けトーストイベントを SignalR で配信する実装。
/// </summary>
public class AppToastSignalRPublisher(
    ILogger<AppToastSignalRPublisher> logger,
    IHubContext<AppEventHub> hubContext) : IAppToastEventPublisher
{
    /// <summary>
    /// トーストイベントを全クライアントに配信する。
    /// </summary>
    /// <param name="payload">配信内容</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public async ValueTask PublishAsync(AppToastEvent payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(AppEventHubMethods.Toast, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"全画面トーストイベントの配信に失敗しました。");
        }
    }
}
