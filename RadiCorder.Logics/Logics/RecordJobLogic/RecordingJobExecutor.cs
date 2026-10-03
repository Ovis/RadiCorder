using RadiCorder.Logics.Domain.Recording;
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

using RadiCorder.Logics.BackgroundServices;

namespace RadiCorder.Logics.Logics.RecordJobLogic;

/// <summary>
/// キュー投入済みの録音ジョブを、ジョブ単位のスコープで実行する。
/// </summary>
public class RecordingJobExecutor(
    ILogger<RecordingScheduleBackgroundService> logger,
    RadioDbContext dbContext,
    RecordingLobLogic recordingLobLogic,
    NotificationLobLogic notificationLobLogic,
    IAppConfigurationService appConfigurationService)
{
    /// <summary>
    /// キュー投入済みジョブを実行する。
    /// </summary>
    public async ValueTask ExecuteAsync(Ulid jobId, CancellationToken cancellationToken)
    {
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
                await MarkJobFailedAsync(dbContext, job, RecordingJobErrorClassifier.ClassifyError(error), error?.Message, cancellationToken);
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
            await MarkJobFailedAsync(dbContext, job, RecordingJobErrorClassifier.ClassifyError(ex), ex.Message, cancellationToken);
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
