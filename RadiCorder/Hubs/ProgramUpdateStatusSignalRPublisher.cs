using Microsoft.AspNetCore.SignalR;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using ZLogger;

namespace RadiCorder.Hubs;

/// <summary>
/// 番組表更新状態を SignalR で配信する実装。
/// </summary>
public class ProgramUpdateStatusSignalRPublisher(
    ILogger<ProgramUpdateStatusSignalRPublisher> logger,
    IHubContext<ProgramUpdateHub> hubContext) : IProgramUpdateStatusPublisher
{
    /// <summary>
    /// 更新状態を全クライアントに配信する。
    /// </summary>
    /// <param name="status">更新状態</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public async ValueTask PublishAsync(ProgramUpdateStatusSnapshot status, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(ProgramUpdateHubMethods.ProgramUpdateStatusChanged, status, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"番組表更新状態イベントの配信に失敗しました。");
        }
    }
}
