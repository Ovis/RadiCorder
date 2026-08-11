using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Primitives;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 番組表更新の日次スケジュールを管理するバックグラウンドサービス。
/// </summary>
public class ProgramUpdateScheduleBackgroundService(
    ILogger<ProgramUpdateScheduleBackgroundService> logger,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    private readonly TimeZoneInfo _japanTimeZone = JapanTimeZone.Resolve();
    private DateOnly? _lastTriggeredDate;

    /// <summary>
    /// サービス本体のループ処理。
    /// </summary>
    /// <param name="stoppingToken">停止トークン</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"番組表更新スケジューラサービスを開始しました。");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TryRunScheduledUpdateAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"番組表更新スケジューラの実行判定でエラーが発生しました。");
            }
        }

        logger.ZLogInformation($"番組表更新スケジューラサービスを終了しました。");
    }

    /// <summary>
    /// 毎日 08:00(JST) の番組表更新実行可否を判定する。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    private async ValueTask TryRunScheduledUpdateAsync(CancellationToken cancellationToken)
    {
        var nowJst = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _japanTimeZone);
        var today = DateOnly.FromDateTime(nowJst.Date);

        if (_lastTriggeredDate.HasValue && _lastTriggeredDate.Value == today)
        {
            return;
        }

        if (nowJst.Hour != 8 || nowJst.Minute != 0)
        {
            return;
        }

        _lastTriggeredDate = today;

        using var scope = serviceScopeFactory.CreateScope();
        var programUpdateRunner = scope.ServiceProvider.GetRequiredService<ProgramUpdateRunner>();
        await programUpdateRunner.ExecuteAsync("scheduled", cancellationToken);
    }
}
