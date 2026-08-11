using Microsoft.AspNetCore.SignalR;
using RadiCorder.Logics.Domain.Reserve;
using ZLogger;

namespace RadiCorder.Hubs;

/// <summary>
/// 録音予定更新イベントを SignalR で配信する実装。
/// </summary>
public class ReserveScheduleSignalRPublisher(
    ILogger<ReserveScheduleSignalRPublisher> logger,
    IHubContext<ReserveHub> hubContext) : IReserveScheduleEventPublisher
{
    /// <summary>
    /// 録音予定更新イベントを全クライアントに配信する。
    /// </summary>
    /// <param name="payload">通知ペイロード</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public async ValueTask PublishAsync(ReserveScheduleChangedEvent payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(ReserveHubMethods.ReserveScheduleChanged, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"録音予定更新イベントの配信に失敗しました。");
        }
    }
}
