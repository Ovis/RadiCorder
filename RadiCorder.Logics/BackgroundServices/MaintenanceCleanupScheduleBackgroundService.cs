using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// ログ/一時ファイルの定期クリーンアップを実行するバックグラウンドサービス。
/// </summary>
public class MaintenanceCleanupScheduleBackgroundService(
    ILogger<MaintenanceCleanupScheduleBackgroundService> logger,
    IServiceScopeFactory serviceScopeFactory,
    IAppConfigurationService appConfigurationService) : BackgroundService
{
    private readonly TimeZoneInfo _japanTimeZone = JapanTimeZone.Resolve();
    private DateOnly? _lastTriggeredDate;

    /// <summary>
    /// サービス本体のループ処理。
    /// </summary>
    /// <param name="stoppingToken">停止トークン</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"メンテナンスクリーンアップスケジューラサービスを開始しました。");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TryRunScheduledCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"メンテナンスクリーンアップの実行判定でエラーが発生しました。");
            }
        }

        logger.ZLogInformation($"メンテナンスクリーンアップスケジューラサービスを終了しました。");
    }

    /// <summary>
    /// 毎日 04:00(JST) のクリーンアップ実行可否を判定する。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    private async ValueTask TryRunScheduledCleanupAsync(CancellationToken cancellationToken)
    {
        var nowJst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _japanTimeZone);
        var today = DateOnly.FromDateTime(nowJst.Date);

        if (_lastTriggeredDate.HasValue && _lastTriggeredDate.Value == today)
        {
            return;
        }

        if (nowJst.Hour != 4 || nowJst.Minute != 0)
        {
            return;
        }

        _lastTriggeredDate = today;

        using var scope = serviceScopeFactory.CreateScope();
        var logMaintenanceLobLogic = scope.ServiceProvider.GetRequiredService<LogMaintenanceLobLogic>();
        var temporaryStorageMaintenanceLobLogic = scope.ServiceProvider.GetRequiredService<TemporaryStorageMaintenanceLobLogic>();

        await logMaintenanceLobLogic.CleanupOldLogFilesAsync(appConfigurationService.LogRetentionDays, cancellationToken);
        await temporaryStorageMaintenanceLobLogic.CleanupAsync(cancellationToken);
    }
}
