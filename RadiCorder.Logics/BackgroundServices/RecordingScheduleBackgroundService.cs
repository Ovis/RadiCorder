using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Domain.Recording;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Infrastructure.Recording;
using ZLogger;

namespace RadiCorder.Logics.BackgroundServices;

/// <summary>
/// 録音予約ジョブの実行を担当するバックグラウンドサービス。
/// </summary>
public class RecordingScheduleBackgroundService(
    ILogger<RecordingScheduleBackgroundService> logger,
    IServiceScopeFactory serviceScopeFactory,
    IAppConfigurationService appConfigurationService,
    IRecordingScheduleWakeup recordingScheduleWakeup) : BackgroundService
{
    private static readonly TimeSpan PeriodicScanInterval = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<Ulid, byte> RunningJobMap = new();
    private readonly ConcurrentDictionary<Ulid, Task> RunningTasks = new();

    /// <summary>
    /// サービス本体。
    /// </summary>
    /// <param name="stoppingToken">停止トークン</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"録音スケジューラサービスを開始しました。");

        await RecoverJobsOnStartupAsync(stoppingToken);

        try
        {
            var nextPeriodicAtUtc = DateTimeOffset.UtcNow.Add(PeriodicScanInterval);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await QueueDueJobsAsync(stoppingToken);
                    var nextDelay = await CalculateNextOneShotDelayAsync(stoppingToken);
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var wakeupTask = recordingScheduleWakeup.WaitAsync(waitCts.Token).AsTask();
                    var nowUtc = DateTimeOffset.UtcNow;

                    while (nextPeriodicAtUtc <= nowUtc)
                    {
                        nextPeriodicAtUtc = nextPeriodicAtUtc.Add(PeriodicScanInterval);
                    }

                    var periodicDelay = nextPeriodicAtUtc - nowUtc;
                    var periodicTask = Task.Delay(periodicDelay, waitCts.Token);

                    if (!nextDelay.HasValue)
                    {
                        var completedTask = await Task.WhenAny(periodicTask, wakeupTask);
                        await waitCts.CancelAsync();

                        if (completedTask == wakeupTask)
                        {
                            logger.ZLogDebug($"録音スケジューラが起床通知で再評価を再開します。");
                        }

                        continue;
                    }

                    var oneShotTask = Task.Delay(nextDelay.Value, waitCts.Token);
                    var completed = await Task.WhenAny(periodicTask, oneShotTask, wakeupTask);
                    await waitCts.CancelAsync();

                    if (completed == wakeupTask)
                    {
                        logger.ZLogDebug($"録音スケジューラが起床通知で再評価を再開します。");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.ZLogError(ex, $"録音スケジューラループでエラーが発生しました。");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }

        }
        finally
        {
            await Task.WhenAll(RunningTasks.Values);
        }

        logger.ZLogInformation($"録音スケジューラサービスを終了しました。");
    }

    /// <summary>
    /// 起動時に中断状態のジョブを復旧する。
    /// </summary>
    private async ValueTask RecoverJobsOnStartupAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var programScheduleLobLogic = scope.ServiceProvider.GetRequiredService<ProgramScheduleLobLogic>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioDbContext>();

        await new RecordingDeletionJournal(appConfigurationService).RecoverAsync(dbContext, logger, cancellationToken);

        var finalizationRecovery = scope.ServiceProvider.GetService<RecordingFinalizationRecovery>();
        if (finalizationRecovery != null)
        {
            await finalizationRecovery.RecoverAsync(cancellationToken);
        }

        // DB上の有効ジョブをスケジューラ実行可能な初期状態へ揃える。
        await programScheduleLobLogic.SetScheduleJobFromDbAsync();

        var recovery = new RecordingJobRecovery(dbContext, appConfigurationService,
            scope.ServiceProvider.GetService<RecordingFinalizationJournal>());
        await recovery.RecoverAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>
    /// 準備開始時刻を過ぎたジョブをキュー投入して実行開始する。
    /// </summary>
    private async ValueTask QueueDueJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        var dueJobIds = await dbContext.ScheduleJob
            .Where(x => x.IsEnabled)
            .Where(x => x.State == ScheduleJobState.Pending)
            .Where(x => x.PrepareStartUtc <= nowUtc)
            .OrderBy(x => x.PrepareStartUtc)
            .Select(x => x.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var jobId in dueJobIds)
        {
            logger.ZLogDebug($"録音スケジューラが実行対象ジョブを検出しました。 jobId={jobId}");

            // 前の実行が終了する前にPendingへ戻っても、Queuedだけを残さない。
            if (!RunningJobMap.TryAdd(jobId, 0)) continue;

            int updated;
            try
            {
                updated = await dbContext.ScheduleJob
                .Where(x => x.Id == jobId && x.State == ScheduleJobState.Pending && x.IsEnabled)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, ScheduleJobState.Queued)
                    .SetProperty(x => x.QueuedAtUtc, nowUtc), cancellationToken);
            }
            catch { RunningJobMap.TryRemove(jobId, out _); throw; }

            if (updated != 1)
            {
                logger.ZLogDebug($"録音スケジューラがジョブのキュー投入をスキップしました。 jobId={jobId} reason=state_changed");
                RunningJobMap.TryRemove(jobId, out _);
                continue;
            }

            foreach (var completed in RunningTasks.Where(x => x.Value.IsCompleted).ToList()) RunningTasks.TryRemove(completed.Key, out _);
            RunningTasks[jobId] = Task.Run(async () =>
            {
                try
                {
                    await ExecuteQueuedJobAsync(jobId, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.ZLogError(ex, $"録音ジョブの実行でエラーが発生しました。 jobId={jobId}");
                }
                finally
                {
                    RunningJobMap.TryRemove(jobId, out _);
                }
            });
        }
    }

    /// <summary>
    /// 次回監視までの待機時間を計算する。
    /// </summary>
    private async ValueTask<TimeSpan?> CalculateNextOneShotDelayAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        var nowUtc = DateTimeOffset.UtcNow;

        var nextPrepareStartUtc = await dbContext.ScheduleJob
            .Where(x => x.IsEnabled && x.State == ScheduleJobState.Pending)
            .MinAsync(x => (DateTimeOffset?)x.PrepareStartUtc, cancellationToken);

        if (!nextPrepareStartUtc.HasValue)
        {
            return null;
        }

        var nextDelay = nextPrepareStartUtc.Value - nowUtc;
        if (nextDelay <= TimeSpan.Zero)
        {
            return TimeSpan.FromMilliseconds(200);
        }

        return nextDelay;
    }

    /// <summary>
    /// キュー投入済みジョブを独立スコープで実行する。
    /// </summary>
    private async ValueTask ExecuteQueuedJobAsync(Ulid jobId, CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<RecordingJobExecutor>();
        await executor.ExecuteAsync(jobId, cancellationToken);
    }


    /// <summary>
    /// 準備開始時刻を UTC で算出する。
    /// </summary>
    private DateTimeOffset ResolvePrepareStartUtc(ScheduleJob job)
    {
        var fireAtUtc = ResolveFireAtUtc(job);
        return fireAtUtc - RecordingScheduleTiming.PreparingLeadTime;
    }

    /// <summary>
    /// 録音開始時刻を UTC で算出する。
    /// </summary>
    private DateTimeOffset ResolveFireAtUtc(ScheduleJob job)
    {
        var startDelay = job.StartDelay ?? appConfigurationService.RecordStartDuration;
        var nowUtc = DateTimeOffset.UtcNow;
        return RecordingScheduleTiming.ResolveFireAtUtc(
            job.RecordingType, job.StartDateTime, job.EndDateTime, startDelay, nowUtc)
            ?? nowUtc;
    }

}
