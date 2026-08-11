using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 類似録音抽出の定期実行を行うバックグラウンドサービス。
/// </summary>
public class DuplicateDetectionScheduleBackgroundService(
    ILogger<DuplicateDetectionScheduleBackgroundService> logger,
    IServiceScopeFactory serviceScopeFactory,
    IAppConfigurationService appConfigurationService) : BackgroundService
{
    private readonly TimeZoneInfo _japanTimeZone = JapanTimeZone.Resolve();
    private DateTimeOffset? _lastTriggeredMinuteUtc;

    /// <summary>
    /// サービス本体のループ処理。
    /// </summary>
    /// <param name="stoppingToken">停止トークン</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"類似録音抽出のスケジューラサービスを開始しました。");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TryRunScheduledDuplicateDetectionAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"類似録音抽出スケジューラの実行判定でエラーが発生しました。");
            }
        }

        logger.ZLogInformation($"類似録音抽出のスケジューラサービスを終了しました。");
    }

    /// <summary>
    /// 設定値に応じて類似録音抽出を実行する。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    private async ValueTask TryRunScheduledDuplicateDetectionAsync(CancellationToken cancellationToken)
    {
        // UTC分単位で1回のみ判定し、同じ分での重複実行を防ぐ。
        var nowUtc = DateTimeOffset.UtcNow;
        var currentMinuteUtc = new DateTimeOffset(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            nowUtc.Hour,
            nowUtc.Minute,
            0,
            TimeSpan.Zero);

        if (_lastTriggeredMinuteUtc.HasValue && _lastTriggeredMinuteUtc.Value == currentMinuteUtc)
        {
            return;
        }

        if (appConfigurationService.DuplicateDetectionIntervalDays <= 0)
        {
            return;
        }

        var nowJst = TimeZoneInfo.ConvertTime(nowUtc, _japanTimeZone);
        var configuredDayOfWeek = appConfigurationService.DuplicateDetectionScheduleDayOfWeek;
        var configuredHour = appConfigurationService.DuplicateDetectionScheduleHour;
        var configuredMinute = appConfigurationService.DuplicateDetectionScheduleMinute;

        if ((int)nowJst.DayOfWeek != configuredDayOfWeek ||
            nowJst.Hour != configuredHour ||
            nowJst.Minute != configuredMinute)
        {
            return;
        }

        _lastTriggeredMinuteUtc = currentMinuteUtc;

        using var scope = serviceScopeFactory.CreateScope();
        var duplicateDetectionLobLogic = scope.ServiceProvider.GetRequiredService<RecordedDuplicateDetectionLobLogic>();
        await duplicateDetectionLobLogic.ExecuteAsync(
            triggerSource: "scheduled",
            lookbackDays: 30,
            maxPhase1Groups: 100,
            phase2Mode: "light",
            broadcastClusterWindowHours: 48,
            cancellationToken: cancellationToken);
    }
}
