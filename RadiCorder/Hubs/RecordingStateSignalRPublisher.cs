using Microsoft.AspNetCore.SignalR;
using RadiCorder.Logics.Domain.Recording;
using ZLogger;

namespace RadiCorder.Hubs;

/// <summary>
/// 録音状態変更イベントを SignalR で配信する実装。
/// </summary>
public class RecordingStateSignalRPublisher(
    ILogger<RecordingStateSignalRPublisher> logger,
    IHubContext<RecordingHub> hubContext) : IRecordingStateEventPublisher
{
    /// <summary>
    /// 録音状態変更イベントを全クライアントに配信する。
    /// </summary>
    /// <param name="payload">通知ペイロード</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    public async ValueTask PublishAsync(RecordingStateChangedEvent payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.All.SendAsync(RecordingHubMethods.RecordingStateChanged, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"録音状態変更イベントの配信に失敗しました。");
        }
    }
}
