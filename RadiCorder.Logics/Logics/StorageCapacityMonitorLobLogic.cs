using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics;

/// <summary>
/// 保存先ストレージ空き容量を監視し、しきい値を下回った場合に通知する。
/// </summary>
public class StorageCapacityMonitorLobLogic(
    ILogger<StorageCapacityMonitorLobLogic> logger,
    IAppConfigurationService appConfigurationService,
    NotificationLobLogic notificationLobLogic)
{
    /// <summary>
    /// 空き容量を確認し、必要に応じて通知する。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async ValueTask CheckAndNotifyLowDiskSpaceAsync(CancellationToken cancellationToken = default)
    {
        var saveDir = appConfigurationService.RecordFileSaveDir;
        if (string.IsNullOrWhiteSpace(saveDir) || !Directory.Exists(saveDir))
        {
            logger.ZLogWarning($"保存先ディレクトリが存在しないため、空き容量監視をスキップします。path={saveDir}");
            return;
        }

        var thresholdMb = NormalizePositive(appConfigurationService.StorageLowSpaceThresholdMb, 1024);
        var cooldownHours = NormalizePositive(appConfigurationService.StorageLowSpaceNotificationCooldownHours, 24);

        var rootPath = Path.GetPathRoot(Path.GetFullPath(saveDir));
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            logger.ZLogWarning($"保存先ディレクトリのルート解決に失敗したため、空き容量監視をスキップします。path={saveDir}");
            return;
        }

        var driveInfo = new DriveInfo(rootPath);
        if (!driveInfo.IsReady)
        {
            logger.ZLogWarning($"保存先ストレージが未準備のため、空き容量監視をスキップします。root={rootPath}");
            return;
        }

        var availableMb = driveInfo.AvailableFreeSpace / (1024d * 1024d);
        if (availableMb > thresholdMb)
        {
            return;
        }

        var lastNotifiedAt = await appConfigurationService.GetStorageLowSpaceLastNotifiedAtAsync();
        var nowUtc = DateTimeOffset.UtcNow;
        if (lastNotifiedAt.HasValue && nowUtc < lastNotifiedAt.Value.AddHours(cooldownHours))
        {
            logger.ZLogDebug($"空き容量不足通知をクールダウン中のためスキップします。last={lastNotifiedAt} cooldownHours={cooldownHours}");
            return;
        }

        var message = $"録音保存先の空き容量が不足しています。現在 {availableMb:F0} MB（しきい値 {thresholdMb} MB）";
        await notificationLobLogic.SetNotificationAsync(
            LogLevel.Warning,
            NoticeCategory.StorageLowSpace,
            message);

        await appConfigurationService.UpdateStorageLowSpaceLastNotifiedAtAsync(nowUtc);
        logger.ZLogWarning($"空き容量不足通知を登録しました。availableMb={availableMb:F0} thresholdMb={thresholdMb}");
    }

    private static int NormalizePositive(int value, int fallback)
    {
        return value > 0 ? value : fallback;
    }
}
