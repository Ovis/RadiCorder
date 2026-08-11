using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 保存先ストレージ空き容量監視を定期実行するバックグラウンドサービス。
/// </summary>
public class StorageCapacityMonitorBackgroundService(
    ILogger<StorageCapacityMonitorBackgroundService> logger,
    IServiceScopeFactory serviceScopeFactory,
    IAppConfigurationService appConfigurationService) : BackgroundService
{
    /// <summary>
    /// サービス本体のループ処理。
    /// </summary>
    /// <param name="stoppingToken">停止トークン</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"ストレージ空き容量監視サービスを開始しました。");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var storageCapacityMonitorLobLogic = scope.ServiceProvider.GetRequiredService<StorageCapacityMonitorLobLogic>();
                await storageCapacityMonitorLobLogic.CheckAndNotifyLowDiskSpaceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"ストレージ空き容量監視処理でエラーが発生しました。");
            }

            var intervalMinutes = Math.Clamp(appConfigurationService.StorageLowSpaceCheckIntervalMinutes, 1, 1440);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }

        logger.ZLogInformation($"ストレージ空き容量監視サービスを終了しました。");
    }
}
