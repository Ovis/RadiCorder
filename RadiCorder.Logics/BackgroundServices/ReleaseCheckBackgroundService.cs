using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.NotificationLogic;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 新しいリリース確認を定期実行するバックグラウンドサービス。
/// </summary>
public class ReleaseCheckBackgroundService(
    ILogger<ReleaseCheckBackgroundService> logger,
    IServiceScopeFactory serviceScopeFactory) : BackgroundService
{
    /// <summary>
    /// サービス本体のループ処理。
    /// </summary>
    /// <param name="stoppingToken">停止トークン</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"リリース確認サービスを開始しました。");

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var releaseCheckLobLogic = scope.ServiceProvider.GetRequiredService<ReleaseCheckLobLogic>();
                await releaseCheckLobLogic.CheckForNewReleaseAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"リリース確認処理でエラーが発生しました。");
            }
        }

        logger.ZLogInformation($"リリース確認サービスを終了しました。");
    }
}
