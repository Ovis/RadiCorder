using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.ReserveLogic
{
    public partial class ReserveLobLogic(
        ILogger<ReserveLobLogic> logger,
        IRadioAppContext appContext,
        IAppConfigurationService appConfig,
        IReserveRepository reserveRepository,
        IProgramScheduleRepository programScheduleRepository,
        RecordJobLobLogic recordJobLobLogic,
        ProgramScheduleLobLogic programScheduleLobLogic,
        NotificationLobLogic notificationLobLogic,
        TagLobLogic tagLobLogic,
        IEntryMapper entryMapper,
        IReserveScheduleEventPublisher? reserveScheduleEventPublisher = null)
    {
        /// <summary>
        /// 録音予約リスト取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<(bool IsSuccess, List<ScheduleEntry>? Entry, Exception? Error)> GetReserveListAsync()
        {
            try
            {
                var list = await reserveRepository.GetScheduleJobsAsync();
                var entries = list.Select(job => entryMapper.ToScheduleEntry(job)).ToList();

                if (list.Count == 0)
                {
                    return (true, entries, null);
                }

                var keywordReserveIdsByScheduleJobId =
                    await reserveRepository.GetKeywordReserveIdsByScheduleJobIdsAsync(list.Select(x => x.Id));

                var allKeywordReserveIds = keywordReserveIdsByScheduleJobId
                    .Values
                    .SelectMany(x => x)
                    .Distinct()
                    .ToList();

                if (allKeywordReserveIds.Count == 0)
                {
                    return (true, entries, null);
                }

                var keywordReserves = await reserveRepository.GetKeywordReservesAsync();
                var keywordReserveById = keywordReserves
                    .Where(x => allKeywordReserveIds.Contains(x.Id))
                    .ToDictionary(x => x.Id, x => x);

                var tagNamesByReserveId = await tagLobLogic.GetKeywordReserveTagNameMapAsync(allKeywordReserveIds);

                var entryByJobId = entries.ToDictionary(x => x.Id, x => x);
                var jobById = list.ToDictionary(x => x.Id, x => x);

                foreach (var (scheduleJobId, reserveIds) in keywordReserveIdsByScheduleJobId)
                {
                    if (!entryByJobId.TryGetValue(scheduleJobId, out var entry))
                    {
                        continue;
                    }

                    var matchedReserves = reserveIds
                        .Where(keywordReserveById.ContainsKey)
                        .Select(id => keywordReserveById[id])
                        .OrderBy(x => x.SortOrder)
                        .ThenBy(x => x.Id)
                        .ToList();

                    entry.MatchedKeywordReserveKeywords = matchedReserves
                        .Select(x => x.Keyword)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .ToList();

                    var targetReserveIds = KeywordReserveTagMergeEvaluator.ResolveTargetReserveIds(
                        matchedReserves.Select(x => (x.Id, x.SortOrder, x.MergeTagBehavior)).ToList(),
                        appConfig.MergeTagsFromAllMatchedKeywordRules,
                        jobById.TryGetValue(scheduleJobId, out var scheduleJob) ? scheduleJob.KeywordReserveId : null);

                    entry.PlannedTagNames = targetReserveIds
                        .Where(tagNamesByReserveId.ContainsKey)
                        .SelectMany(id => tagNamesByReserveId[id])
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                }

                return (true, entries, null);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"予約状況の取得に失敗しました。");
                return (false, null, ex);
            }
        }



        /// <summary>
        /// 番組IDをもとに録音ジョブを登録
        /// </summary>
        /// <param name="programId"></param>
        /// <param name="serviceKind"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async ValueTask<(bool IsSuccess, Exception? Error)> SetRecordingJobByProgramIdAsync(string programId, RadioServiceKind serviceKind, RecordingType type)
        {
            if (!programScheduleLobLogic.SupportsProgramLookup(serviceKind))
                return (false, new DomainException("未対応のサービスです。"));
            var entry = await programScheduleLobLogic.GetProgramAsync(programId, serviceKind);
            if (entry == null) return (false, new DomainException("指定された番組が番組表にありませんでした。"));

            if (type == RecordingType.OnDemand)
            {
                if (serviceKind != RadioServiceKind.Radiru)
                {
                    return (false, new DomainException("聞き逃し配信録音はらじる★らじるのみ対応です。"));
                }

                var nowUtc = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(entry.OnDemandContentUrl))
                {
                    return (false, new DomainException("聞き逃し配信URLが見つかりません。番組表更新後に再度お試しください。"));
                }

                if (!entry.OnDemandExpiresAtUtc.HasValue || entry.OnDemandExpiresAtUtc.Value <= nowUtc)
                {
                    return (false, new DomainException("聞き逃し配信の有効期限が切れています。"));
                }
            }



            var job = new ScheduleJob
            {
                Id = Ulid.NewUlid(),
                ServiceKind = serviceKind,
                StationId = entry.StationId,
                ProgramId = entry.ProgramId,
                FilePath = "",
                StartDateTime = entry.StartTime.ToUniversalTime(),
                EndDateTime = entry.EndTime.ToUniversalTime(),
                Title = entry.Title,
                Performer = entry.Performer,
                Description = entry.Description,
                RecordingType = type,
                ReserveType = ReserveType.Program,
                IsEnabled = true
            };


            var currentDate = appContext.StandardDateTimeOffset;
            // 放送中のリアルタイム録音は即時実行ジョブとして登録する
            if (type != RecordingType.TimeFree && type != RecordingType.OnDemand &&
                entry.StartTime < currentDate && entry.EndTime > currentDate)
            {
                job.RecordingType = RecordingType.Immediate;
            }

            // 放送後の番組はタイムフリー録音として登録する
            if (entry.EndTime < currentDate && type != RecordingType.OnDemand)
            {
                job.RecordingType = RecordingType.TimeFree;
            }

            if (job.RecordingType == RecordingType.TimeFree)
            {
                logger.ZLogInformation(
                    $"タイムフリー録音予約を作成します。 programId={entry.ProgramId} title={entry.Title} station={entry.StationId} requestedType={type} actualType={job.RecordingType} now={currentDate:O} programStart={entry.StartTime:O} programEnd={entry.EndTime:O}");
            }

            try
            {
                await reserveRepository.AddScheduleJobAsync(job);

                var result = await recordJobLobLogic.SetScheduleJobAsync(job);

                if (!result.IsSuccess)
                {
                    logger.ZLogError(result.Error, $"録音予約に失敗 {programId}", programId);

                    return (false, new DomainException("録音予約に失敗しました。"));
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"録音予約に失敗 {programId}", programId);

                return (false, new DomainException("録音予約に失敗しました。"));
            }

            await PublishReserveScheduleChangedSafeAsync();
            return (true, default);
        }



        /// <summary>
        /// 番組単位予約の予約削除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async ValueTask<(bool IsSuccess, Exception? Error)> DeleteProgramReserveEntryAsync(Ulid id)
        {
            try
            {
                var job = await reserveRepository.GetScheduleJobByIdAsync(id);

                if (job == null)
                {
                    // 既に削除済み・処理済みの場合は冪等に成功扱いとする
                    return (true, null);
                }

                // スケジューラからジョブを削除
                await recordJobLobLogic.DeleteScheduleJobAsync(job.Id);

                try
                {
                    await reserveRepository.RemoveScheduleJobAsync(job);
                }
                catch
                {
                    // 実行中処理との競合で既に削除されていた場合も成功扱い
                }
                await PublishReserveScheduleChangedSafeAsync();

                return (true, null);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"予約の削除に失敗しました。");

                return (false, ex);
            }
        }


        /// <summary>
        /// すでに予約済みの番組の番組のマージンを更新
        /// </summary>
        /// <returns></returns>
        public async ValueTask UpdateReserveDurationAsync()
        {
            try
            {
                var scheduleJob =
                    await reserveRepository.GetScheduleJobsNeedingDurationUpdateAsync();

                if (!scheduleJob.Any())
                    return;

                foreach (var job in scheduleJob)
                {
                    await recordJobLobLogic.DeleteScheduleJobAsync(job.Id);
                    await recordJobLobLogic.SetScheduleJobAsync(job);
                }

                await PublishReserveScheduleChangedSafeAsync();
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"古い予約情報の削除処理に失敗");
            }
        }


        /// <summary>
        /// 録音処理が行われなかった古い予約の削除
        /// </summary>
        /// <returns></returns>
        public async ValueTask DeleteOldReserveEntryAsync()
        {
            try
            {
                var scheduleJob =
                    await reserveRepository.GetScheduleJobsOlderThanAsync(appContext.StandardDateTimeOffset.AddDays(-1));

                if (!scheduleJob.Any())
                    return;

                foreach (var job in scheduleJob)
                {
                    await recordJobLobLogic.DeleteScheduleJobAsync(job.Id);
                }

                await reserveRepository.RemoveScheduleJobsAsync(scheduleJob);
                await PublishReserveScheduleChangedSafeAsync();
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"古い予約情報の削除処理に失敗");
            }
        }

        /// <summary>
        /// 録音予定更新イベント通知を安全に実行する。
        /// </summary>
        protected async ValueTask PublishReserveScheduleChangedSafeAsync()
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
    }

}


