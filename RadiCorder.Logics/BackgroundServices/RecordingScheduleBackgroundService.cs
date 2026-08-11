using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RecordingLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
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
    private static readonly TimeSpan StartupRecoveryTimeout = TimeSpan.FromHours(2);
    private static readonly TimeSpan TimeFreeReadyLeadTime = TimeSpan.FromMinutes(3);
    private static readonly ConcurrentDictionary<Ulid, byte> RunningJobMap = new();

    /// <summary>
    /// サービス本体。
    /// </summary>
    /// <param name="stoppingToken">停止トークン</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.ZLogInformation($"録音スケジューラサービスを開始しました。");

        await RecoverJobsOnStartupAsync(stoppingToken);

        var nextPeriodicAtUtc = DateTimeOffset.UtcNow.Add(PeriodicScanInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await QueueDueJobsAsync(stoppingToken);
                var nextDelay = await CalculateNextOneShotDelayAsync(stoppingToken);
                var wakeupTask = recordingScheduleWakeup.WaitAsync(stoppingToken).AsTask();
                var nowUtc = DateTimeOffset.UtcNow;

                while (nextPeriodicAtUtc <= nowUtc)
                {
                    nextPeriodicAtUtc = nextPeriodicAtUtc.Add(PeriodicScanInterval);
                }

                var periodicDelay = nextPeriodicAtUtc - nowUtc;
                var periodicTask = Task.Delay(periodicDelay, stoppingToken);

                if (!nextDelay.HasValue)
                {
                    var completedTask = await Task.WhenAny(periodicTask, wakeupTask);

                    if (completedTask == wakeupTask)
                    {
                        logger.ZLogDebug($"録音スケジューラが起床通知で再評価を再開します。");
                    }

                    continue;
                }

                var oneShotTask = Task.Delay(nextDelay.Value, stoppingToken);
                var completed = await Task.WhenAny(periodicTask, oneShotTask, wakeupTask);

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

        // DB上の有効ジョブをスケジューラ実行可能な初期状態へ揃える。
        await programScheduleLobLogic.SetScheduleJobFromDbAsync();

        var interruptedStates = new[]
        {
            ScheduleJobState.Queued,
            ScheduleJobState.Preparing,
            ScheduleJobState.Recording,
            ScheduleJobState.Finalizing
        };

        var nowUtc = DateTimeOffset.UtcNow;
        var targets = await dbContext.ScheduleJob
            .Where(x => x.IsEnabled)
            .Where(x => interruptedStates.Contains(x.State))
            .ToListAsync(cancellationToken);

        foreach (var job in targets)
        {
            var isTooOld = nowUtc - job.StartDateTime.ToUniversalTime() > StartupRecoveryTimeout;
            if (isTooOld)
            {
                job.State = ScheduleJobState.Failed;
                job.LastErrorCode = ScheduleJobErrorCode.StartupRecoveryTimeout;
                job.LastErrorDetail = "起動時復旧でタイムアウトしたため失敗扱いにしました。";
                job.CompletedUtc = nowUtc;
                job.IsEnabled = false;
                continue;
            }

            // 起動直後に取りこぼしなく再評価できるよう Pending へ戻す。
            job.State = ScheduleJobState.Pending;
            job.QueuedAtUtc = null;
            job.ActualStartUtc = null;
            job.CompletedUtc = null;
            job.PrepareStartUtc = ResolvePrepareStartUtc(job);
            if (job.PrepareStartUtc < nowUtc)
            {
                job.PrepareStartUtc = nowUtc;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
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

            var updated = await dbContext.ScheduleJob
                .Where(x => x.Id == jobId && x.State == ScheduleJobState.Pending && x.IsEnabled)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, ScheduleJobState.Queued)
                    .SetProperty(x => x.QueuedAtUtc, nowUtc), cancellationToken);

            if (updated != 1)
            {
                logger.ZLogDebug($"録音スケジューラがジョブのキュー投入をスキップしました。 jobId={jobId} reason=state_changed");
                continue;
            }

            if (!RunningJobMap.TryAdd(jobId, 0))
            {
                logger.ZLogDebug($"録音スケジューラがジョブのキュー投入をスキップしました。 jobId={jobId} reason=already_running");
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteQueuedJobAsync(jobId, cancellationToken);
                }
                finally
                {
                    RunningJobMap.TryRemove(jobId, out _);
                }
            }, cancellationToken);
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
    /// キュー投入済みジョブを実行する。
    /// </summary>
    private async ValueTask ExecuteQueuedJobAsync(Ulid jobId, CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        var recordingLobLogic = scope.ServiceProvider.GetRequiredService<RecordingLobLogic>();
        var notificationLobLogic = scope.ServiceProvider.GetRequiredService<NotificationLobLogic>();

        var job = await dbContext.ScheduleJob
            .Where(x => x.Id == jobId && x.IsEnabled)
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null)
        {
            logger.ZLogDebug($"録音ジョブを開始できませんでした。 jobId={jobId} reason=not_found_or_disabled");
            return;
        }

        logger.ZLogDebug(
            $"録音ジョブの実行準備を開始します。 jobId={jobId} programId={job.ProgramId} title={job.Title} recordingType={job.RecordingType} start={job.StartDateTime:O} end={job.EndDateTime:O} prepareStartUtc={job.PrepareStartUtc:O}");

        var preparingUpdated = await dbContext.ScheduleJob
            .Where(x => x.Id == jobId && x.State == ScheduleJobState.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, ScheduleJobState.Preparing), cancellationToken);
        if (preparingUpdated != 1)
        {
            logger.ZLogDebug($"録音ジョブの実行準備を開始できませんでした。 jobId={jobId} reason=not_queued");
            return;
        }

        var fireAtUtc = ResolveFireAtUtc(job);
        var wait = fireAtUtc - DateTimeOffset.UtcNow;
        logger.ZLogDebug($"録音ジョブの実行時刻を評価しました。 jobId={jobId} fireAtUtc={fireAtUtc:O} waitMs={Math.Max(0, (long)wait.TotalMilliseconds)}");
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }

        var recordingUpdated = await dbContext.ScheduleJob
            .Where(x => x.Id == jobId && x.State == ScheduleJobState.Preparing)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, ScheduleJobState.Recording)
                .SetProperty(x => x.ActualStartUtc, DateTimeOffset.UtcNow), cancellationToken);
        if (recordingUpdated != 1)
        {
            logger.ZLogDebug($"録音ジョブを録音状態へ遷移できませんでした。 jobId={jobId} reason=not_preparing");
            return;
        }

        logger.ZLogDebug($"録音ジョブを開始します。 jobId={jobId}");

        using var recordCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        RecordingCancellationRegistry.Register(jobId.ToString(), recordCts);

        try
        {
            var startDelaySeconds = job.StartDelay?.TotalSeconds ?? appConfigurationService.RecordStartDuration.TotalSeconds;
            var endDelaySeconds = job.EndDelay?.TotalSeconds ?? appConfigurationService.RecordEndDuration.TotalSeconds;
            string? outputDirectoryRelativePathOverride = null;
            string? outputFileNameTemplateOverride = null;

            if (job.ReserveType == ReserveType.Keyword && job.KeywordReserveId != null)
            {
                var keywordReserve = await dbContext.KeywordReserve
                    .AsNoTracking()
                    .Where(x => x.Id == job.KeywordReserveId.Value)
                    .Select(x => new { x.FolderPath, x.FileName })
                    .FirstOrDefaultAsync(cancellationToken);

                if (keywordReserve != null)
                {
                    outputDirectoryRelativePathOverride = keywordReserve.FolderPath;
                    outputFileNameTemplateOverride = keywordReserve.FileName;
                }
            }

            await notificationLobLogic.SetNotificationAsync(
                logLevel: LogLevel.Information,
                category: NoticeCategory.RecordingStart,
                message: $"{job.Title} の録音を開始します。");

            var (isSuccess, error) = await recordingLobLogic.RecordRadioAsync(
                serviceKind: job.ServiceKind,
                programId: job.ProgramId,
                programName: job.Title,
                scheduleJobId: job.Id.ToString(),
                isTimeFree: job.RecordingType == RecordingType.TimeFree,
                isOnDemand: job.RecordingType == RecordingType.OnDemand,
                startDelay: startDelaySeconds,
                endDelay: endDelaySeconds,
                outputDirectoryRelativePathOverride: outputDirectoryRelativePathOverride,
                outputFileNameTemplateOverride: outputFileNameTemplateOverride,
                deleteScheduleOnFinish: false,
                cancellationToken: recordCts.Token);

            if (!isSuccess)
            {
                await MarkJobFailedAsync(dbContext, job, ClassifyError(error), error?.Message, cancellationToken);
                return;
            }

            await dbContext.ScheduleJob
                .Where(x => x.Id == jobId && x.State == ScheduleJobState.Recording)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.State, ScheduleJobState.Finalizing), cancellationToken);

            try
            {
                dbContext.ScheduleJob.Remove(job);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"録音後処理でScheduleJob削除に失敗しました。 jobId={jobId}");
                await MarkJobFailedAsync(dbContext, job, ScheduleJobErrorCode.FinalizeFailed, ex.Message, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            await MarkJobFailedAsync(dbContext, job, ScheduleJobErrorCode.Cancelled, "録音ジョブがキャンセルされました。", cancellationToken, isCancelled: true);
            await notificationLobLogic.SetNotificationAsync(
                logLevel: LogLevel.Warning,
                category: NoticeCategory.RecordingCancel,
                message: $"{job.Title} の録音をキャンセルしました。");
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"録音ジョブ実行で例外が発生しました。 jobId={jobId}");
            await MarkJobFailedAsync(dbContext, job, ClassifyError(ex), ex.Message, cancellationToken);
        }
        finally
        {
            RecordingCancellationRegistry.Unregister(jobId.ToString());
        }
    }

    /// <summary>
    /// ジョブ失敗情報を記録する。
    /// </summary>
    private static async ValueTask MarkJobFailedAsync(
        RadioDbContext dbContext,
        ScheduleJob job,
        ScheduleJobErrorCode errorCode,
        string? detail,
        CancellationToken cancellationToken,
        bool isCancelled = false)
    {
        var nextState = isCancelled ? ScheduleJobState.Cancelled : ScheduleJobState.Failed;
        await dbContext.ScheduleJob
            .Where(x => x.Id == job.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.State, nextState)
                .SetProperty(x => x.LastErrorCode, errorCode)
                .SetProperty(x => x.LastErrorDetail, detail)
                .SetProperty(x => x.CompletedUtc, DateTimeOffset.UtcNow)
                .SetProperty(x => x.IsEnabled, false)
                .SetProperty(x => x.RetryCount, x => x.RetryCount + 1), cancellationToken);
    }

    /// <summary>
    /// 準備開始時刻を UTC で算出する。
    /// </summary>
    private DateTimeOffset ResolvePrepareStartUtc(ScheduleJob job)
    {
        var fireAtUtc = ResolveFireAtUtc(job);
        return fireAtUtc.AddSeconds(-10);
    }

    /// <summary>
    /// 録音開始時刻を UTC で算出する。
    /// </summary>
    private DateTimeOffset ResolveFireAtUtc(ScheduleJob job)
    {
        var startDelaySeconds = job.StartDelay?.TotalSeconds ?? appConfigurationService.RecordStartDuration.TotalSeconds;
        var nowUtc = DateTimeOffset.UtcNow;
        var timeFreeReadyAtUtc = job.EndDateTime.ToUniversalTime().Add(TimeFreeReadyLeadTime);
        return job.RecordingType switch
        {
            RecordingType.TimeFree => timeFreeReadyAtUtc > nowUtc ? timeFreeReadyAtUtc : nowUtc,
            RecordingType.OnDemand => nowUtc,
            RecordingType.Immediate => nowUtc,
            RecordingType.RealTime => job.StartDateTime.AddSeconds(-startDelaySeconds).AddSeconds(-1).ToUniversalTime(),
            _ => nowUtc
        };
    }

    /// <summary>
    /// 例外を失敗分類へ変換する。
    /// </summary>
    private static ScheduleJobErrorCode ClassifyError(Exception? exception)
    {
        if (exception == null)
        {
            return ScheduleJobErrorCode.Unknown;
        }

        if (exception is OperationCanceledException)
        {
            return ScheduleJobErrorCode.Cancelled;
        }

        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return ScheduleJobErrorCode.Unknown;
        }

        if (message.Contains("認証", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.AuthFailed;
        }

        if (message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.FfmpegFailed;
        }

        if (message.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("容量", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.DiskFull;
        }

        if (message.Contains("io", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("I/O", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.IoError;
        }

        if (message.Contains("source", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("playlist", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("配信", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.SourceUnavailable;
        }

        return ScheduleJobErrorCode.Unknown;
    }
}
