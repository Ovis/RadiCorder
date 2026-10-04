using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Domain.Reserve;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Logics.ReserveLogic
{
    public partial class ReserveLobLogic
    {
        /// <summary>
        /// キーワード録音予約リスト取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<(bool IsSuccess, List<KeywordReserveEntry>? Entry, Exception? Error)> GetKeywordReserveListAsync()
        {
            try
            {
                var (keywordReserves, keywordReserveRadioStations) = await GetKeywordReserveListFromDbAsync();

                var keywordReserveEntries = keywordReserves.Select(x => entryMapper.ToKeywordReserveEntry(x)).ToList();
                var allTags = await tagLobLogic.GetTagsAsync(string.Empty);
                var tagNameById = allTags.ToDictionary(t => t.Id, t => t.Name);

                foreach (var entry in keywordReserveEntries)
                {
                    var stations = keywordReserveRadioStations
                        .Where(krs => krs.Id == entry.Id)
                        .ToList();

                    entry.SelectedRadikoStationIds = stations
                        .Where(krs => krs.RadioServiceKind == RadioServiceKind.Radiko)
                        .Select(krs => krs.RadioStation)
                        .ToList();

                    entry.SelectedRadiruStationIds = stations
                        .Where(krs => krs.RadioServiceKind == RadioServiceKind.Radiru)
                        .Select(krs => krs.RadioStation)
                        .ToList();

                    var tagIds = await tagLobLogic.GetKeywordReserveTagIdsAsync(entry.Id);
                    entry.TagIds = tagIds;
                    entry.Tags = tagIds
                        .Where(tagNameById.ContainsKey)
                        .Select(tagId => tagNameById[tagId])
                        .ToList();
                }

                return (true, keywordReserveEntries, null);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"キーワード予約の取得に失敗しました。");
                return (false, null, ex);
            }
        }




        /// <summary>
        /// 指定されたキーワード予約の更新処理
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        public async ValueTask<(bool IsSuccess, Exception? Error)> UpdateKeywordReserveAsync(KeywordReserveEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.RecordPath) && !entry.RecordPath.IsValidRelativePath())
            {
                return (false, new DomainException("指定された保存先の記載が適切ではありません。"));
            }

            if (!string.IsNullOrEmpty(entry.RecordFileName) && !entry.RecordFileName.IsValidFileName())
            {
                return (false, new DomainException("指定されたファイル名の記載が適切ではありません。"));
            }

            if (entry.SelectedDaysOfWeek.Count == 0)
            {
                return (false, new DomainException("対象曜日を1つ以上選択してください。"));
            }

            try
            {
                // 既存のジョブを削除
                {
                    var (isSuccess, error) = await DeleteKeywordReserveScheduleAsync(entry.Id);
                    if (!isSuccess)
                    {
                        logger.ZLogError(error, $"既存のジョブ削除時に失敗");
                        return (false, error);
                    }
                }

                // 一旦既存の放送局情報を削除
                await DeleteKeywordReserveRadioStationsRecordAsync(entry.Id);

                {
                    var (isSuccess, error) = await UpdateKeywordReserveInternalAsync(entry);

                    if (!isSuccess)
                    {
                        return (false, error);
                    }
                }

                await PublishReserveScheduleChangedSafeAsync();
                return (true, null);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"キーワード予約更新処理に失敗");

                return (false, e);
            }
        }


        /// <summary>
        /// 指定されたキーワード予約の削除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async ValueTask<(bool IsSuccess, Exception? Error)> DeleteKeywordReserveAsync(Ulid id)
        {
            try
            {
                // スケジューラから該当のジョブを削除
                {
                    var (isSuccess, error) = await DeleteKeywordReserveScheduleAsync(id);
                    if (!isSuccess)
                    {
                        logger.ZLogError(error, $"既存のジョブ削除時に失敗");
                        return (false, error);
                    }
                }

                // KeywordReserveRadioStationから該当IDのデータを削除
                await DeleteKeywordReserveRadioStationsRecordAsync(id);

                // KeywordReserveから該当IDのデータを削除
                if (!await DeleteKeywordReserveRecordAsync(id))
                {
                    return (false, new DomainException("指定されたIDのデータが見つかりません。"));
                }

                await PublishReserveScheduleChangedSafeAsync();

            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"キーワード予約の削除に失敗しました。");

                throw;
            }

            return (true, null);
        }





        /// <summary>
        /// キーワード予約処理
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        public async ValueTask<(bool IsSuccess, Exception? Error)> SetKeywordReserveAsync(KeywordReserveEntry entry)
        {
            if (entry.SelectedDaysOfWeek.Count == 0)
            {
                return (false, new DomainException("対象曜日を1つ以上選択してください。"));
            }

            var id = Ulid.NewUlid();

            var daysOfWeek = entry.SelectedDaysOfWeek
                .Select(day => day)
                .Aggregate(DaysOfWeek.None, (a, b) => a | b);

            var keywordReserve = new KeywordReserve
            {
                Id = id,
                Keyword = entry.Keyword,
                ExcludedKeyword = entry.ExcludedKeyword,
                IsTitleOnly = entry.SearchTitleOnly,
                IsExcludeTitleOnly = entry.ExcludeTitleOnly,
                FileName = entry.RecordFileName,
                FolderPath = entry.RecordPath,
                StartTime = entry.StartTime ?? new TimeOnly(0, 0, 0),
                EndTime = entry.EndTime ?? new TimeOnly(23, 59, 59),
                DaysOfWeek = daysOfWeek,
                IsEnable = true,
                StartDelay = entry.StartDelay == null ? null : TimeSpan.FromSeconds(entry.StartDelay.Value),
                EndDelay = entry.EndDelay == null ? null : TimeSpan.FromSeconds(entry.EndDelay.Value),
                SortOrder = await reserveRepository.GetNextKeywordReserveSortOrderAsync(),
                MergeTagBehavior = KeywordReservationPolicy.NormalizeMergeTagBehavior(entry.MergeTagBehavior)
            };

            var radioStation = new List<KeywordReserveRadioStation>();

            {
                var radikoStations = entry.SelectedRadikoStationIds
                    .Select(stationId => new KeywordReserveRadioStation
                    {
                        Id = id,
                        RadioServiceKind = RadioServiceKind.Radiko,
                        RadioStation = stationId
                    })
                    .ToList();

                radioStation.AddRange(radikoStations);
            }

            {
                var radiruStations = entry.SelectedRadiruStationIds
                    .Select(stationId => new KeywordReserveRadioStation
                    {
                        Id = id,
                        RadioServiceKind = RadioServiceKind.Radiru,
                        RadioStation = stationId
                    })
                    .ToList();

                radioStation.AddRange(radiruStations);
            }

            try
            {
                await reserveRepository.AddKeywordReserveAsync(keywordReserve, radioStation);
                await tagLobLogic.SetKeywordReserveTagsAsync(id, entry.TagIds);

                await SetRadioProgramScheduleInternalAsync(keywordReserve, radioStation);
                await PublishReserveScheduleChangedSafeAsync();
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"キーワード予約処理に失敗");

                return (false, e);
            }

            return (true, null);
        }


        /// <summary>
        /// キーワード予約情報をもとに番組予約を行う
        /// </summary>
        /// <returns></returns>
        public async ValueTask SetAllKeywordReserveScheduleAsync()
        {
            var (keywordReserves, stations)
                = await GetKeywordReserveListFromDbAsync();

            foreach (var keywordReserve in keywordReserves)
            {
                await SetRadioProgramScheduleAsync(
                    keywordReserve,
                    stations.Where(r => r.Id == keywordReserve.Id).ToList());
            }
        }


        public async ValueTask<(bool IsSuccess, Exception? Error)> SwitchKeywordReserveEntryStatusAsync(Ulid id)
        {
            try
            {
                var scheduleJob = await reserveRepository.GetScheduleJobSnapshotAsync(id);

                if (scheduleJob == null)
                {
                    return (false, new DomainException("指定されたIDの予約データが見つかりません。"));
                }

                if (scheduleJob.IsEnabled)
                {
                    if (!await reserveRepository.TryDisableScheduleJobAsync(id))
                        throw new DomainException("録音予約の状態が変わったため無効化できませんでした。");
                    await recordJobLobLogic.CancelScheduleJobAndWaitAsync(id);
                }
                else
                {
                    await recordJobLobLogic.CancelScheduleJobAndWaitAsync(id);
                    scheduleJob = await reserveRepository.GetScheduleJobSnapshotAsync(id)
                        ?? throw new DomainException("録音予約が削除されています。");
                    var expectedState = scheduleJob.State;
                    recordJobLobLogic.PrepareForReactivation(scheduleJob);
                    if (!await reserveRepository.TryReactivateScheduleJobAsync(scheduleJob, expectedState))
                        throw new DomainException("録音予約の状態が変わったため再有効化できませんでした。");
                    recordJobLobLogic.NotifyScheduleChanged();
                }

                await PublishReserveScheduleChangedSafeAsync();

            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"キーワード予約の有効化無効化更新処理に失敗");
                return (false, e);
            }

            return (true, null);
        }



        /// <summary>
        /// キーワード予約更新処理
        /// </summary>
        /// <param name="entry"></param>
        /// <returns></returns>
        private async ValueTask<(bool IsSuccess, Exception? Error)> UpdateKeywordReserveInternalAsync(KeywordReserveEntry entry)
        {
            var record = await reserveRepository.GetKeywordReserveByIdAsync(entry.Id);

            if (record == null)
            {
                return (false, new DomainException("指定されたIDのデータが見つかりません。"));
            }

            var daysOfWeek = entry.SelectedDaysOfWeek
                .Select(day => day)
                .Aggregate(DaysOfWeek.None, (a, b) => a | b);

            record.Keyword = entry.Keyword;
            record.ExcludedKeyword = entry.ExcludedKeyword;
            record.IsTitleOnly = entry.SearchTitleOnly;
            record.IsExcludeTitleOnly = entry.ExcludeTitleOnly;
            record.FileName = entry.RecordFileName;
            record.FolderPath = entry.RecordPath;
            record.StartTime = entry.StartTime ?? new TimeOnly(0, 0, 0);
            record.EndTime = entry.EndTime ?? new TimeOnly(23, 59, 59);
            record.DaysOfWeek = daysOfWeek;
            record.IsEnable = entry.IsEnabled;
            record.StartDelay = entry.StartDelay == null ? null : TimeSpan.FromSeconds(entry.StartDelay.Value);
            record.EndDelay = entry.EndDelay == null ? null : TimeSpan.FromSeconds(entry.EndDelay.Value);
            record.MergeTagBehavior = KeywordReservationPolicy.NormalizeMergeTagBehavior(entry.MergeTagBehavior);

            var radioStation = new List<KeywordReserveRadioStation>();

            {
                var radikoStations = entry.SelectedRadikoStationIds
                    .Select(stationId => new KeywordReserveRadioStation
                    {
                        Id = entry.Id,
                        RadioServiceKind = RadioServiceKind.Radiko,
                        RadioStation = stationId
                    })
                    .ToList();

                radioStation.AddRange(radikoStations);
            }

            {
                var radiruStations = entry.SelectedRadiruStationIds
                    .Select(stationId => new KeywordReserveRadioStation
                    {
                        Id = entry.Id,
                        RadioServiceKind = RadioServiceKind.Radiru,
                        RadioStation = stationId
                    })
                    .ToList();

                radioStation.AddRange(radiruStations);
            }

            await reserveRepository.UpdateKeywordReserveAsync(record, radioStation);
            await tagLobLogic.SetKeywordReserveTagsAsync(entry.Id, entry.TagIds);

            if (entry.IsEnabled)
                await SetRadioProgramScheduleInternalAsync(record, radioStation);

            return (true, null);
        }


        /// <summary>
        /// DBからキーワード予約情報のリストを取得
        /// </summary>
        /// <returns></returns>
        private async ValueTask<(List<KeywordReserve> KeywordReserves, List<KeywordReserveRadioStation> Stations)>
            GetKeywordReserveListFromDbAsync()
        {
            try
            {
                var keywordReserves = await reserveRepository.GetKeywordReservesAsync();
                var stations = await reserveRepository.GetKeywordReserveRadioStationsAsync();

                return (keywordReserves, stations);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"キーワード予約の取得に失敗しました。");
                throw;
            }
        }


        /// <summary>
        /// スケジュール処理
        /// </summary>
        /// <param name="keywordReserve"></param>
        /// <param name="radioStation"></param>
        /// <returns></returns>
        private async ValueTask SetRadioProgramScheduleAsync(KeywordReserve keywordReserve, List<KeywordReserveRadioStation> radioStation)
        {
            try
            {
                await SetRadioProgramScheduleInternalAsync(keywordReserve, radioStation);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"キーワード予約の登録に失敗");

                await notificationLobLogic.SetNotificationAsync(
                    logLevel: LogLevel.Error,
                    category: NoticeCategory.KeywordReserveError,
                    message: $"キーワード予約の登録に失敗しました。キーワード:{keywordReserve.Keyword}"
                );
            }
        }


        private async ValueTask SetRadioProgramScheduleInternalAsync(KeywordReserve keywordReserve, List<KeywordReserveRadioStation> radioStation)
        {
            var radikoStationIds = radioStation
                .Where(r => r.RadioServiceKind == RadioServiceKind.Radiko)
                .Select(r => r.RadioStation)
                .Distinct()
                .ToList();

            var radiruStationIds = radioStation
                .Where(r => r.RadioServiceKind == RadioServiceKind.Radiru)
                .Select(r => r.RadioStation)
                .Distinct()
                .ToList();

            var selectedDays = Enum.GetValues<DaysOfWeek>()
                .Where(d => d != DaysOfWeek.None && keywordReserve.DaysOfWeek.HasFlag(d))
                .ToList();

            var programList = new List<RadioProgramEntry>();

            // radiko
            if (radikoStationIds.Count != 0)
            {
                var searchEntity = new ProgramSearchEntity
                {
                    SelectedRadikoStationIds = radikoStationIds,
                    Keyword = keywordReserve.Keyword ?? string.Empty,
                    ExcludedKeyword = keywordReserve.ExcludedKeyword ?? string.Empty,
                    SearchTitleOnly = keywordReserve.IsTitleOnly,
                    SearchTitleOnlyExcludedKeyword = keywordReserve.IsExcludeTitleOnly,
                    SelectedDaysOfWeek = selectedDays,
                    StartTime = keywordReserve.StartTime,
                    EndTime = keywordReserve.EndTime,
                    IncludeHistoricalPrograms = false
                };

                var radikoPrograms = await programScheduleRepository.SearchRadikoProgramsAsync(
                    searchEntity,
                    appContext.StandardDateTimeOffset);

                var currentUtc = appContext.StandardDateTimeOffset.UtcDateTime;
                programList.AddRange(
                    radikoPrograms
                        .Where(p =>
                            p.StartTime > currentUtc ||
                            (p.StartTime <= currentUtc &&
                             p.EndTime > currentUtc))
                        .Select(p => entryMapper.ToRadioProgramEntry(p)));
            }

            // らじる★らじる
            if (radiruStationIds.Count != 0)
            {
                var searchEntity = new ProgramSearchEntity
                {
                    SelectedRadiruStationIds = radiruStationIds,
                    Keyword = keywordReserve.Keyword ?? string.Empty,
                    ExcludedKeyword = keywordReserve.ExcludedKeyword ?? string.Empty,
                    SearchTitleOnly = keywordReserve.IsTitleOnly,
                    SearchTitleOnlyExcludedKeyword = keywordReserve.IsExcludeTitleOnly,
                    SelectedDaysOfWeek = selectedDays,
                    StartTime = keywordReserve.StartTime,
                    EndTime = keywordReserve.EndTime,
                    IncludeHistoricalPrograms = false
                };

                var radiruPrograms = await programScheduleRepository.SearchRadiruProgramsAsync(
                    searchEntity,
                    appContext.StandardDateTimeOffset);

                var currentUtc = appContext.StandardDateTimeOffset.UtcDateTime;
                var filtered = radiruPrograms
                    .Where(p => p.StartTime > currentUtc)
                    .ToList();

                var result = filtered
                    .GroupBy(p => p.EventId)
                    .Select(group =>
                        group.FirstOrDefault(p => p.AreaId == appConfig.RadiruArea)
                                     ?? group.First())
                    .ToList();

                programList.AddRange(result.Select(p => entryMapper.ToRadioProgramEntry(p)));
            }

            List<ScheduleJob> scheduleJobs = [];
            List<ScheduleJobKeywordReserveRelation> scheduleJobKeywordReserveRelations = [];

            foreach (var p in programList)
            {
                var existingScheduleJob = await reserveRepository.GetScheduleJobByProgramKeyAsync(new ProgramKey(p.ServiceKind, p.ProgramId));

                // 既存予約がある場合、キーワード予約由来であれば関連のみ追加してスキップ
                if (existingScheduleJob != null)
                {
                    if (existingScheduleJob.ReserveType == ReserveType.Keyword)
                    {
                        scheduleJobKeywordReserveRelations.Add(new ScheduleJobKeywordReserveRelation
                        {
                            ScheduleJobId = existingScheduleJob.Id,
                            KeywordReserveId = keywordReserve.Id
                        });

                        if (await ShouldPromotePrimaryKeywordReserveAsync(existingScheduleJob, keywordReserve))
                        {
                            existingScheduleJob.KeywordReserveId = keywordReserve.Id;
                            existingScheduleJob.FilePath = keywordReserve.FolderPath;
                            existingScheduleJob.StartDelay = keywordReserve.StartDelay;
                            existingScheduleJob.EndDelay = keywordReserve.EndDelay;

                            await reserveRepository.UpdateScheduleJobAsync(existingScheduleJob);

                            if (existingScheduleJob.IsEnabled && existingScheduleJob.State == ScheduleJobState.Pending)
                            {
                                await recordJobLobLogic.SetScheduleJobAsync(existingScheduleJob);
                            }
                        }
                    }

                    continue;
                }

                var job = new ScheduleJob
                {
                    Id = Ulid.NewUlid(),
                    KeywordReserveId = keywordReserve.Id,
                    ServiceKind = p.ServiceKind,
                    StationId = p.StationId,
                    AreaId = p.AreaId,
                    ProgramId = p.ProgramId,
                    FilePath = keywordReserve.FolderPath,
                    StartDateTime = p.StartTime.ToUniversalTime(),
                    EndDateTime = p.EndTime.ToUniversalTime(),
                    Title = p.Title,
                    Subtitle = p.Subtitle,
                    Performer = p.Performer,
                    Description = p.Description,
                    IsEnabled = true,
                    StartDelay = keywordReserve.StartDelay,
                    EndDelay = keywordReserve.EndDelay,
                    RecordingType = KeywordReservationPolicy.ResolveKeywordReserveRecordingType(p),
                    ReserveType = ReserveType.Keyword
                };

                scheduleJobs.Add(job);
                scheduleJobKeywordReserveRelations.Add(new ScheduleJobKeywordReserveRelation
                {
                    ScheduleJobId = job.Id,
                    KeywordReserveId = keywordReserve.Id
                });
            }

            if (scheduleJobs.Count > 0)
            {
                await reserveRepository.AddScheduleJobsAsync(scheduleJobs);
            }

            if (scheduleJobKeywordReserveRelations.Count > 0)
            {
                await reserveRepository.AddScheduleJobKeywordReserveRelationsAsync(scheduleJobKeywordReserveRelations);
            }

            if (scheduleJobs.Count > 0)
            {
                await recordJobLobLogic.SetScheduleJobsAsync(scheduleJobs);
            }
        }


        /// <summary>
        /// キーワード予約レコードの削除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private async ValueTask<bool> DeleteKeywordReserveRecordAsync(Ulid id)
        {
            return await reserveRepository.DeleteKeywordReserveAsync(id);
        }


        /// <summary>
        /// キーワード予約対象放送局の削除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>

        private async ValueTask DeleteKeywordReserveRadioStationsRecordAsync(Ulid id)
        {
            // KeywordReserveRadioStationから該当IDのデータを削除
            await reserveRepository.DeleteKeywordReserveRadioStationsAsync(id);
        }

        /// <summary>
        /// スケジュールされているジョブの削除
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private async ValueTask<(bool IsSuccess, Exception? Error)> DeleteKeywordReserveScheduleAsync(Ulid id)
        {
            var scheduleJobs =
                await reserveRepository.GetScheduleJobsByKeywordReserveIdAsync(id);

            if (scheduleJobs.Count == 0)
            {
                return (true, null);
            }

            var scheduleJobIds = scheduleJobs.Select(x => x.Id).ToList();
            var reserveIdsMap = await reserveRepository.GetKeywordReserveIdsByScheduleJobIdsAsync(scheduleJobIds);

            var orphanJobs = scheduleJobs
                .Where(job =>
                {
                    if (!reserveIdsMap.TryGetValue(job.Id, out var reserveIds))
                    {
                        return true;
                    }

                    return reserveIds.Count == 0 || reserveIds.All(x => x == id);
                })
                .ToList();

            var (isSuccess, error) = await recordJobLobLogic.DeleteScheduleJobsAsync(orphanJobs);

            if (!isSuccess)
            {
                return (false, Error: error);
            }

            await reserveRepository.RemoveKeywordReserveFromScheduleJobsAsync(id, scheduleJobIds);

            // 関連が残らない予約のみDBから削除
            if (orphanJobs.Count > 0)
            {
                await reserveRepository.RemoveScheduleJobsAsync(orphanJobs);
            }

            return (true, null);
        }

        public async ValueTask<(bool IsSuccess, Exception? Error)> ReorderKeywordReservesAsync(IReadOnlyList<Ulid> orderedIds)
        {
            if (orderedIds.Count == 0)
            {
                return (false, new DomainException("並び順の更新対象がありません。"));
            }

            try
            {
                var existingIds = (await reserveRepository.GetKeywordReservesAsync())
                    .Select(x => x.Id)
                    .ToHashSet();

                if (orderedIds.Distinct().Count() != orderedIds.Count)
                {
                    return (false, new DomainException("並び順データに重複があります。"));
                }

                if (orderedIds.Any(id => !existingIds.Contains(id)))
                {
                    return (false, new DomainException("並び順データに存在しないルールが含まれています。"));
                }

                if (orderedIds.Count != existingIds.Count)
                {
                    return (false, new DomainException("並び順データが不足しています。"));
                }

                await reserveRepository.ReorderKeywordReservesAsync(orderedIds);
                await RefreshPendingKeywordScheduleJobsAsync();

                return (true, null);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"キーワード予約の並び順更新に失敗しました。");
                return (false, e);
            }
        }


        private async ValueTask RefreshPendingKeywordScheduleJobsAsync()
        {
            var pendingKeywordJobs = (await reserveRepository.GetScheduleJobsAsync())
                .Where(x => x.ReserveType == ReserveType.Keyword && x.State == ScheduleJobState.Pending)
                .ToList();

            if (pendingKeywordJobs.Count == 0)
            {
                return;
            }

            var reserveIdsMap = await reserveRepository.GetKeywordReserveIdsByScheduleJobIdsAsync(pendingKeywordJobs.Select(x => x.Id));
            if (reserveIdsMap.Count == 0)
            {
                return;
            }

            var allReserveIds = reserveIdsMap.Values
                .SelectMany(x => x)
                .Distinct()
                .ToList();

            if (allReserveIds.Count == 0)
            {
                return;
            }

            var reserveById = (await reserveRepository.GetKeywordReservesAsync())
                .Where(x => allReserveIds.Contains(x.Id))
                .ToDictionary(x => x.Id, x => x);

            foreach (var job in pendingKeywordJobs)
            {
                if (!reserveIdsMap.TryGetValue(job.Id, out var relatedReserveIds))
                {
                    continue;
                }

                var primaryReserve = KeywordReservationPolicy.ResolvePrimaryKeywordReserve(
                    relatedReserveIds
                        .Where(reserveById.ContainsKey)
                        .Select(id => reserveById[id])
                        .ToList());

                if (primaryReserve == null)
                {
                    continue;
                }

                var isChanged =
                    job.KeywordReserveId != primaryReserve.Id ||
                    !string.Equals(job.FilePath, primaryReserve.FolderPath, StringComparison.Ordinal) ||
                    job.StartDelay != primaryReserve.StartDelay ||
                    job.EndDelay != primaryReserve.EndDelay;

                if (!isChanged)
                {
                    continue;
                }

                job.KeywordReserveId = primaryReserve.Id;
                job.FilePath = primaryReserve.FolderPath;
                job.StartDelay = primaryReserve.StartDelay;
                job.EndDelay = primaryReserve.EndDelay;

                await reserveRepository.UpdateScheduleJobAsync(job);

                if (job.IsEnabled)
                {
                    await recordJobLobLogic.SetScheduleJobAsync(job);
                }
            }
        }

        private async ValueTask<bool> ShouldPromotePrimaryKeywordReserveAsync(ScheduleJob scheduleJob, KeywordReserve candidate)
        {
            if (scheduleJob.KeywordReserveId == null)
            {
                return true;
            }

            if (scheduleJob.KeywordReserveId == candidate.Id)
            {
                return false;
            }

            var currentPrimary = await reserveRepository.GetKeywordReserveByIdAsync(scheduleJob.KeywordReserveId.Value);
            if (currentPrimary == null)
            {
                return true;
            }

            return KeywordReservationPolicy.IsHigherPriority(candidate, currentPrimary);
        }


    }

}
