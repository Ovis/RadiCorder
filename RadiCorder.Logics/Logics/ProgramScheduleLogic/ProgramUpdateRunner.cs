using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Logics.StationLogic;
using ZLogger;

namespace RadiCorder.Logics.Logics.ProgramScheduleLogic;

/// <summary>
/// 番組表更新処理を実行するランナー。
/// </summary>
public class ProgramUpdateRunner(
    ILogger<ProgramUpdateRunner> logger,
    StationLobLogic stationLobLogic,
    RadikoUniqueProcessLogic radikoUniqueProcessLogic,
    ReserveLobLogic reserveLobLogic,
    ProgramScheduleLobLogic programScheduleLobLogic,
    NotificationLobLogic notificationLobLogic,
    IProgramUpdateStatusService programUpdateStatusService,
    IProgramUpdateStatusPublisher? programUpdateStatusPublisher = null,
    IAppToastEventPublisher? appToastEventPublisher = null)
{
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    /// <summary>
    /// 番組表更新処理を実行する。
    /// </summary>
    /// <param name="triggerSource">起動元識別子</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async ValueTask ExecuteAsync(string triggerSource, CancellationToken cancellationToken = default)
    {
        if (!await ExecutionGate.WaitAsync(0, cancellationToken))
        {
            logger.ZLogInformation($"番組表更新は既に実行中のためスキップしました。 source={triggerSource}");
            return;
        }

        try
        {
            await PublishStatusChangedSafeAsync(programUpdateStatusService.MarkStarted(triggerSource), cancellationToken);
            logger.ZLogInformation($"番組表更新を開始します。 source={triggerSource}");
            await notificationLobLogic.SetNotificationAsync(
                logLevel: LogLevel.Information,
                category: NoticeCategory.UpdateProgramStart,
                message: "番組表の更新を開始します。");

            // radiko 側の放送局/番組情報を更新する。
            await radikoUniqueProcessLogic.RefreshRadikoAreaCacheAsync();
            await stationLobLogic.UpsertRadikoStationDefinitionAsync();
            await programScheduleLobLogic.UpdateLatestRadikoProgramDataAsync();
            await programScheduleLobLogic.DeleteOldRadikoProgramAsync();

            // らじる★らじる側の放送局/番組情報を更新する。
            await stationLobLogic.TryUpdateRadiruStationInformationIfDueAsync(cancellationToken);
            await programScheduleLobLogic.UpdateRadiruProgramDataAsync();
            await programScheduleLobLogic.DeleteOldRadiruProgramAsync();

            // 更新後に予約再生成と更新時刻記録を行う。
            await reserveLobLogic.DeleteOldReserveEntryAsync();
            await reserveLobLogic.SetAllKeywordReserveScheduleAsync();
            await programScheduleLobLogic.SetProgramLastUpdateDateTimeAsync();

            await notificationLobLogic.SetNotificationAsync(
                logLevel: LogLevel.Information,
                category: NoticeCategory.UpdateProgramEnd,
                message: "番組表の更新が完了しました。");
            await PublishStatusChangedSafeAsync(programUpdateStatusService.MarkSucceeded(), cancellationToken);
            await PublishGlobalToastSafeAsync(
                message: "番組表の更新が完了しました。",
                isSuccess: true,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"番組表の更新に失敗しました。 source={triggerSource}");
            await notificationLobLogic.SetNotificationAsync(
                logLevel: LogLevel.Error,
                category: NoticeCategory.UpdateProgramError,
                message: "番組表の更新に失敗しました。");
            await PublishStatusChangedSafeAsync(programUpdateStatusService.MarkFailed(), cancellationToken);
            await PublishGlobalToastSafeAsync(
                message: "番組表の更新に失敗しました。",
                isSuccess: false,
                cancellationToken: cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    /// <summary>
    /// 番組表更新状態イベント通知を安全に実行する。
    /// </summary>
    private async ValueTask PublishStatusChangedSafeAsync(ProgramUpdateStatusSnapshot status, CancellationToken cancellationToken)
    {
        if (programUpdateStatusPublisher is null)
        {
            return;
        }

        try
        {
            await programUpdateStatusPublisher.PublishAsync(status, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"番組表更新状態イベント通知に失敗しました。");
        }
    }

    /// <summary>
    /// 全画面トーストイベント通知を安全に実行する。
    /// </summary>
    private async ValueTask PublishGlobalToastSafeAsync(string message, bool isSuccess, CancellationToken cancellationToken)
    {
        if (appToastEventPublisher is null)
        {
            return;
        }

        try
        {
            await appToastEventPublisher.PublishAsync(
                new AppToastEvent(message, isSuccess, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"全画面トーストイベント通知に失敗しました。");
        }
    }
}
