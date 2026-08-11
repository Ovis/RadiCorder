using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.UseCases.Recording;
using ZLogger;

namespace RadiCorder.Logics.Logics.RecordingLogic
{
    /// <summary>
    /// 録音処理のエントリポイント
    /// </summary>
    public class RecordingLobLogic(
        ILogger<RecordingLobLogic> logger,
        RecordingOrchestrator orchestrator,
        RadioDbContext dbContext,
        NotificationLobLogic notificationLobLogic,
        IAppConfigurationService appConfigurationService,
        TagLobLogic tagLobLogic,
        IReserveScheduleEventPublisher? reserveScheduleEventPublisher = null)
    {
        /// <summary>
        /// 録音処理
        /// </summary>
        /// <param name="serviceKind">配信サービス種別</param>
        /// <param name="programId">番組ID</param>
        /// <param name="programName">番組名</param>
        /// <param name="scheduleJobId">スケジュールジョブID</param>
        /// <param name="isTimeFree">タイムフリー録音かどうか</param>
        /// <param name="isOnDemand">聞き逃し配信録音かどうか</param>
        /// <param name="startDelay">開始ディレイ（秒）</param>
        /// <param name="endDelay">終了ディレイ（秒）</param>
        /// <param name="deleteScheduleOnFinish">録音終了後にScheduleJobを削除するか</param>
        /// <param name="cancellationToken"></param>
        /// <returns>成功可否と例外</returns>
        public async ValueTask<(bool IsSuccess, Exception? Error)> RecordRadioAsync(
            RadioServiceKind serviceKind,
            string programId,
            string programName,
            string scheduleJobId,
            bool isTimeFree,
            double startDelay,
            double endDelay,
            bool isOnDemand = false,
            string? outputDirectoryRelativePathOverride = null,
            string? outputFileNameTemplateOverride = null,
            bool deleteScheduleOnFinish = true,
            CancellationToken cancellationToken = default
            )
        {
            try
            {
                // オーケストレーターに録音処理を委譲
                var command = new RecordingCommand(
                    ServiceKind: serviceKind,
                    ProgramId: programId,
                    ProgramName: programName,
                    IsTimeFree: isTimeFree,
                    StartDelaySeconds: startDelay,
                    EndDelaySeconds: endDelay,
                    ScheduleJobId: scheduleJobId,
                    IsOnDemand: isOnDemand,
                    OutputDirectoryRelativePathOverride: outputDirectoryRelativePathOverride,
                    OutputFileNameTemplateOverride: outputFileNameTemplateOverride);

                var result = await orchestrator.RecordAsync(command, cancellationToken);
                if (!result.IsSuccess)
                {
                    var errorMessage = result.ErrorMessage ?? "録音に失敗しました。";
                    await notificationLobLogic.SetNotificationAsync(
                        logLevel: LogLevel.Error,
                        category: NoticeCategory.RecordingError,
                        message: $"{programName} の録音に失敗しました。理由: {errorMessage}");
                    return (false, new DomainException(errorMessage));
                }

                await notificationLobLogic.SetNotificationAsync(
                    logLevel: LogLevel.Information,
                    category: NoticeCategory.RecordingSuccess,
                    message: $"{programName} の録音が完了しました。");

                if (result.RecordingId.HasValue)
                {
                    await TryApplyKeywordReserveTagsAsync(scheduleJobId, result.RecordingId.Value);
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"録音処理で例外発生");

                if (deleteScheduleOnFinish)
                {
                    await DeleteScheduleJobAsync(scheduleJobId, programName);
                }

                return (false, e);
            }

            if (deleteScheduleOnFinish)
            {
                await DeleteScheduleJobAsync(scheduleJobId, programName);
            }

            return (true, null);
        }

        /// <summary>
        /// 録音後のスケジュール削除
        /// </summary>
        /// <param name="scheduleJobId">スケジュールジョブID</param>
        /// <param name="programName">番組名</param>
        private async ValueTask DeleteScheduleJobAsync(string scheduleJobId, string programName)
        {
            if (string.IsNullOrEmpty(scheduleJobId))
                return;

            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            try
            {
                // ScheduleJobテーブルからidに一致するデータを削除する
                var scheduleJob = await dbContext.ScheduleJob.FindAsync(Ulid.Parse(scheduleJobId));

                if (scheduleJob != null)
                {
                    dbContext.ScheduleJob.Remove(scheduleJob);
                    await dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();
                    await PublishReserveScheduleChangedSafeAsync();
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"スケジュールからの削除に失敗");

                await transaction.RollbackAsync();

                await notificationLobLogic.SetNotificationAsync(
                    logLevel: LogLevel.Warning,
                    category: NoticeCategory.RecordingError,
                    message: $"{programName}の録音に成功しましたが、スケジュールからの削除に失敗しました。"
                );
            }
        }

        /// <summary>
        /// 録音予定更新イベント通知を安全に実行する。
        /// </summary>
        private async ValueTask PublishReserveScheduleChangedSafeAsync()
        {
            if (reserveScheduleEventPublisher is null)
            {
                return;
            }

            try
            {
                await reserveScheduleEventPublisher.PublishAsync(new ReserveScheduleChangedEvent(DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, $"録音予定更新イベント通知に失敗しました。");
            }
        }

        /// <summary>
        /// キーワード予約に紐づくタグを録音へ自動付与
        /// </summary>
        private async ValueTask TryApplyKeywordReserveTagsAsync(string scheduleJobId, Ulid recordingId)
        {
            if (string.IsNullOrEmpty(scheduleJobId) || !Ulid.TryParse(scheduleJobId, out var scheduleJobUlid))
            {
                return;
            }

            var schedule = await dbContext.ScheduleJob.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scheduleJobUlid);
            if (schedule == null)
            {
                return;
            }

            try
            {
                var reserveIds = await dbContext.ScheduleJobKeywordReserveRelations
                    .Where(x => x.ScheduleJobId == scheduleJobUlid)
                    .Select(x => x.KeywordReserveId)
                    .ToListAsync();

                if (schedule.KeywordReserveId != null)
                {
                    reserveIds.Add(schedule.KeywordReserveId.Value);
                }

                // 同一番組に複数予約経路がある場合も、関連キーワード予約を補完して
                // タグ統合の取りこぼしを防ぐ。
                var sameProgramRows = await dbContext.ScheduleJob
                    .AsNoTracking()
                    .Where(x => x.ProgramId == schedule.ProgramId && x.ReserveType == ReserveType.Keyword)
                    .Select(x => new { x.Id, x.KeywordReserveId })
                    .ToListAsync();

                reserveIds.AddRange(
                    sameProgramRows
                        .Where(x => x.KeywordReserveId != null)
                        .Select(x => x.KeywordReserveId!.Value));

                var sameProgramScheduleIds = sameProgramRows.Select(x => x.Id).ToList();
                if (sameProgramScheduleIds.Count > 0)
                {
                    var relationReserveIds = await dbContext.ScheduleJobKeywordReserveRelations
                        .Where(x => sameProgramScheduleIds.Contains(x.ScheduleJobId))
                        .Select(x => x.KeywordReserveId)
                        .Distinct()
                        .ToListAsync();

                    reserveIds.AddRange(relationReserveIds);
                }

                var reserveSettings = await dbContext.KeywordReserve
                    .AsNoTracking()
                    .Where(x => reserveIds.Contains(x.Id))
                    .Select(x => new
                    {
                        x.Id,
                        x.MergeTagBehavior,
                        x.SortOrder
                    })
                    .ToListAsync();

                if (reserveSettings.Count == 0)
                {
                    return;
                }
                var targetReserveIds = KeywordReserveTagMergeEvaluator.ResolveTargetReserveIds(
                    reserveSettings.Select(x => (x.Id, x.SortOrder, x.MergeTagBehavior)).ToList(),
                    appConfigurationService.MergeTagsFromAllMatchedKeywordRules,
                    schedule.KeywordReserveId);

                if (targetReserveIds.Count == 0)
                {
                    return;
                }

                var allTagIds = new List<Guid>();
                foreach (var reserveId in targetReserveIds)
                {
                    var tagIds = await tagLobLogic.GetKeywordReserveTagIdsAsync(reserveId);
                    if (tagIds.Count > 0)
                    {
                        allTagIds.AddRange(tagIds);
                    }
                }

                var mergedTagIds = allTagIds.Distinct().ToList();
                if (mergedTagIds.Count == 0)
                {
                    return;
                }

                await tagLobLogic.AddTagsToRecordingAsync(recordingId, mergedTagIds);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"録音へのタグ自動付与に失敗しました。");
            }
        }
    }
}
