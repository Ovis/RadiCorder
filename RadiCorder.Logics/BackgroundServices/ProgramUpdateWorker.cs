using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

public class ProgramUpdateWorker(ProgramUpdateQueue queue, IServiceScopeFactory scopeFactory, ILogger<ProgramUpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var source in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ProgramUpdateRunner>().ExecuteAsync(source, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.ZLogError(ex, $"番組表更新Workerでエラーが発生しました。"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
