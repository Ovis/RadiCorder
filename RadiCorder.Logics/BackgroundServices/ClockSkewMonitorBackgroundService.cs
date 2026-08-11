using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// NTP時刻ずれ監視を定期実行するバックグラウンドサービス。
/// </summary>
public class ClockSkewMonitorBackgroundService(
    ILogger<ClockSkewMonitorBackgroundService> logger,
    IServiceScopeFactory serviceScopeFactory,
    IAppConfigurationService appConfigurationService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"NTP時刻ずれ監視サービスを開始しました。");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (appConfigurationService.ClockSkewMonitoringEnabled)
                {
                    using var scope = serviceScopeFactory.CreateScope();
                    var clockSkewMonitorLobLogic = scope.ServiceProvider.GetRequiredService<ClockSkewMonitorLobLogic>();
                    await clockSkewMonitorLobLogic.CheckAndNotifyClockSkewAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"NTP時刻ずれ監視処理でエラーが発生しました。");
            }

            var intervalHours = Math.Clamp(appConfigurationService.ClockSkewCheckIntervalHours, 1, 168);
            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
        }

        logger.ZLogInformation($"NTP時刻ずれ監視サービスを終了しました。");
    }
}
