using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

public class DuplicateDetectionWorker(DuplicateDetectionQueue queue, IServiceScopeFactory scopeFactory,
    ILogger<DuplicateDetectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var request in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<RecordedDuplicateDetectionLobLogic>().ExecuteAsync(
                        "manual", request.LookbackDays, request.MaxPhase1Groups, request.Phase2Mode,
                        request.BroadcastClusterWindowHours, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.ZLogError(ex, $"類似録音抽出Workerでエラーが発生しました。"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
