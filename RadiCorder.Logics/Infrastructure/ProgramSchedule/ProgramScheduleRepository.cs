using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Errors;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.ProgramSchedule;

/// <summary>
/// 番組表データの永続化を担うリポジトリ実装
/// </summary>
public class ProgramScheduleRepository(
    RadioDbContext dbContext,
    IRecordingScheduleWakeup? recordingScheduleWakeup = null,
    ILogger<ProgramScheduleRepository>? logger = null) : IProgramScheduleRepository
{
    private static readonly TimeZoneInfo JapanStandardTimeZone = JapanTimeZone.Resolve();

    /// <summary>
    /// 指定時刻に放送中のradiko番組を取得する
    /// </summary>
    public async ValueTask<List<RadikoProgram>> GetRadikoNowOnAirAsync(DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default)
    {
        var currentRadioDate = standardDateTimeOffset.ToRadioDate();
        var candidates = await dbContext.RadikoPrograms
            .Where(p =>
                p.RadioDate >= currentRadioDate.AddDays(-1) &&
                p.RadioDate <= currentRadioDate.AddDays(1))
            .AsNoTracking()
            .OrderBy(r => r.StartTime)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(p => standardDateTimeOffset >= p.StartTime && standardDateTimeOffset <= p.EndTime)
            .OrderBy(r => r.StartTime)
            .ToList();
    }

    /// <summary>
    /// 指定時刻に放送中のらじる★らじる番組を取得する
    /// </summary>
    public async ValueTask<List<NhkRadiruProgram>> GetRadiruNowOnAirAsync(DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default)
    {
        var currentRadioDate = standardDateTimeOffset.ToRadioDate();
        var candidates = await dbContext.NhkRadiruPrograms
            .Where(p =>
                p.RadioDate >= currentRadioDate.AddDays(-1) &&
                p.RadioDate <= currentRadioDate.AddDays(1))
            .AsNoTracking()
            .OrderBy(r => r.StartTime)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(p => standardDateTimeOffset >= p.StartTime && standardDateTimeOffset <= p.EndTime)
            .OrderBy(r => r.StartTime)
            .ToList();
    }

    /// <summary>
    /// radiko番組一覧を日付と局で取得する
    /// </summary>
    public async ValueTask<List<RadikoProgram>> GetRadikoProgramsAsync(DateOnly date, string stationId, CancellationToken cancellationToken = default)
    {
        return await dbContext.RadikoPrograms
            .Where(r => r.RadioDate == date)
            .Where(r => r.StationId == stationId)
            .OrderBy(r => r.StartTime)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// radiko番組をIDで取得する
    /// </summary>
    public async ValueTask<RadikoProgram?> GetRadikoProgramByIdAsync(string programId, CancellationToken cancellationToken = default)
    {
        return await dbContext.RadikoPrograms
            .AsNoTracking()
            .Where(r => r.ProgramId == programId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// radiko放送局ID一覧を取得する
    /// </summary>
    public async ValueTask<List<string>> GetRadikoStationIdsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.RadikoStations
            .AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => r.StationId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// radiko番組を追加・更新する。同じ局・開始日時の番組IDは予約のため維持する。
    /// </summary>
    public async ValueTask AddRadikoProgramsIfMissingAsync(IEnumerable<RadikoProgram> programs, CancellationToken cancellationToken = default)
    {
        var programList = programs
            .GroupBy(x => x.ProgramId)
            .Select(g => g.Last())
            .GroupBy(x => (x.StationId, x.StartTime))
            .Select(g => g.Last())
            .ToList();
        if (programList.Count == 0)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updatedJobs = 0;

        try
        {
            var programIds = programList
                .Select(x => x.ProgramId)
                .Distinct()
                .ToList();
            var stationIds = programList.Select(p => p.StationId).Distinct().ToList();
            var radioDates = programList.Select(p => p.RadioDate).Distinct().ToList();
            var existingPrograms = await dbContext.RadikoPrograms
                .Where(p => programIds.Contains(p.ProgramId) ||
                    (stationIds.Contains(p.StationId) && radioDates.Contains(p.RadioDate)))
                .ToListAsync(cancellationToken);
            var existingById = existingPrograms.ToDictionary(p => p.ProgramId, StringComparer.Ordinal);
            var existingBySlot = existingPrograms
                .GroupBy(p => (p.StationId, p.StartTime))
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ProgramId, StringComparer.Ordinal).First());

            foreach (var program in programList)
            {
                if (!existingById.TryGetValue(program.ProgramId, out var existing))
                    existingBySlot.TryGetValue((program.StationId, program.StartTime), out existing);

                if (existing != null)
                {
                    if (existing.StationId != program.StationId)
                        throw new DomainException("radiko番組IDが異なる局と重複しています。");

                    var scheduleChanged = existing.StartTime != program.StartTime || existing.EndTime != program.EndTime ||
                        existing.Title != program.Title || existing.Performer != program.Performer || existing.Description != program.Description;
                    var oldSlot = (existing.StationId, existing.StartTime);
                    // 終了日時の訂正で生成IDが変わっても、既存の予約・録音履歴の参照先を変えない。
                    var entry = dbContext.Entry(existing);
                    var values = entry.CurrentValues.Clone();
                    values.SetValues(program);
                    values[nameof(RadikoProgram.ProgramId)] = existing.ProgramId;
                    entry.CurrentValues.SetValues(values);
                    if (existingBySlot.TryGetValue(oldSlot, out var oldSlotProgram) && ReferenceEquals(oldSlotProgram, existing))
                        existingBySlot.Remove(oldSlot);
                    existingBySlot[(existing.StationId, existing.StartTime)] = existing;
                    if (scheduleChanged)
                        updatedJobs += await UpdatePendingRadikoJobsAsync(existing, cancellationToken);
                }
                else
                {
                    await dbContext.RadikoPrograms.AddAsync(program, cancellationToken);
                    existingById[program.ProgramId] = program;
                    existingBySlot[(program.StationId, program.StartTime)] = program;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            DetachTrackedEntities<RadikoProgram>();
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            DetachTrackedEntities<RadikoProgram>();
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        if (updatedJobs > 0)
        {
            try { recordingScheduleWakeup?.Wake(); }
            catch (Exception ex) { logger?.ZLogWarning(ex, $"番組表更新後の録音スケジューラ起床通知に失敗しました。"); }
        }
    }

    /// <summary>
    /// 待機中の番組予約のみ最新情報へ追随させる。実行中・完了済みのジョブは変更しない。
    /// </summary>
    private async Task<int> UpdatePendingRadikoJobsAsync(RadikoProgram program, CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var timeFreePrepareStart = RecordingScheduleTiming.ResolveFireAtUtc(
            RecordingType.TimeFree, program.StartTime, program.EndTime, TimeSpan.Zero, nowUtc)!.Value
            - RecordingScheduleTiming.PreparingLeadTime;
        var jobs = await dbContext.ScheduleJob.AsNoTracking()
            .Where(job => job.ServiceKind == RadioServiceKind.Radiko && job.StationId == program.StationId &&
                job.ProgramId == program.ProgramId && job.State == ScheduleJobState.Pending &&
                (job.ReserveType == ReserveType.Program || job.ReserveType == ReserveType.Keyword))
            .Select(job => new { job.Id, job.RecordingType, job.StartDateTime, job.PrepareStartUtc })
            .ToListAsync(cancellationToken);
        var updated = 0;
        foreach (var job in jobs)
        {
            var prepareStart = job.RecordingType switch
            {
                RecordingType.TimeFree => timeFreePrepareStart,
                RecordingType.RealTime => job.PrepareStartUtc + (program.StartTime - job.StartDateTime),
                _ => job.PrepareStartUtc
            };
            // 読み取り後に実行開始されても、状態を巻き戻したり実行中の時刻を変更したりしない。
            updated += await dbContext.ScheduleJob
                .Where(current => current.Id == job.Id && current.State == ScheduleJobState.Pending &&
                    current.ServiceKind == RadioServiceKind.Radiko && current.StationId == program.StationId &&
                    current.ProgramId == program.ProgramId && current.RecordingType == job.RecordingType)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(current => current.StartDateTime, program.StartTime)
                    .SetProperty(current => current.EndDateTime, program.EndTime)
                    .SetProperty(current => current.Title, program.Title)
                    .SetProperty(current => current.Performer, program.Performer)
                    .SetProperty(current => current.Description, program.Description)
                    .SetProperty(current => current.PrepareStartUtc, prepareStart), cancellationToken);
        }
        return updated;
    }

    /// <summary>
    /// 全放送局について、指定日までのradiko番組表データが揃っているかを判定する
    /// </summary>
    public async ValueTask<bool> HasRadikoProgramsForAllStationsThroughAsync(DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        var stationIds = await dbContext.RadikoStations
            .AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => r.StationId)
            .ToListAsync(cancellationToken);

        if (stationIds.Count == 0)
        {
            return false;
        }

        var maxRadioDateByStation = await dbContext.RadikoPrograms
            .AsNoTracking()
            .Where(r => stationIds.Contains(r.StationId))
            .GroupBy(r => r.StationId)
            .Select(g => new
            {
                StationId = g.Key,
                MaxRadioDate = g.Max(x => x.RadioDate)
            })
            .ToListAsync(cancellationToken);

        var maxDateLookup = maxRadioDateByStation.ToDictionary(x => x.StationId, x => x.MaxRadioDate);

        foreach (var stationId in stationIds)
        {
            if (!maxDateLookup.TryGetValue(stationId, out var maxDate))
            {
                return false;
            }

            if (maxDate < targetDate)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// radiko番組を検索する
    /// </summary>
    public async ValueTask<List<RadikoProgram>> SearchRadikoProgramsAsync(
        ProgramSearchEntity searchEntity,
        DateTimeOffset standardDateTimeOffset,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.RadikoPrograms
            .AsNoTracking()
            .AsQueryable();

        if (searchEntity.SelectedRadikoStationIds.Count != 0)
        {
            query = query.Where(p => searchEntity.SelectedRadikoStationIds.Contains(p.StationId));
        }

        if (!string.IsNullOrWhiteSpace(searchEntity.Keyword))
        {
            var keywords = searchEntity.Keyword.ParseKeywords();

            if (searchEntity.SearchTitleOnly)
            {
                query = query.Where(p => keywords.All(keyword => p.Title.Contains(keyword)));
            }
            else
            {
                query = query.Where(
                    p =>
                        keywords.All(keyword =>
                            p.Title.Contains(keyword) ||
                            p.Performer.Contains(keyword) ||
                            p.Description.Contains(keyword))
                );
            }
        }

        if (!string.IsNullOrWhiteSpace(searchEntity.ExcludedKeyword))
        {
            var excludedKeywords = searchEntity.ExcludedKeyword.ParseKeywords();

            if (searchEntity.SearchTitleOnlyExcludedKeyword)
            {
                query = query.Where(p => !excludedKeywords.Any(excluded => p.Title.Contains(excluded)));
            }
            else
            {
                query = query.Where(p =>
                    !excludedKeywords.Any(excluded =>
                        p.Title.Contains(excluded) ||
                        p.Performer.Contains(excluded) ||
                        p.Description.Contains(excluded))
                );
            }
        }

        if (searchEntity.SelectedDaysOfWeek.Count != 0)
        {
            var selectedDays = searchEntity.SelectedDaysOfWeek.Aggregate(DaysOfWeek.None, (acc, day) => acc | day);
            query = query.Where(p => (p.DaysOfWeek & selectedDays) != DaysOfWeek.None);
        }

        var limitRadioDate = standardDateTimeOffset.AddDays(-7).ToRadioDate();

        // DateTimeOffset のSQL比較はSQLiteで期待どおりにならない場合があるため、
        // 終了済み判定はアプリ側で評価する。
        var list = (await query
                .Where(r => r.RadioDate >= limitRadioDate)
                .ToListAsync(cancellationToken))
            .Where(
                r =>
                    (searchEntity.IncludeHistoricalPrograms || r.EndTime >= standardDateTimeOffset) &&
                    IsProgramWithinSearchTimeRange(
                        r.StartTime,
                        r.EndTime,
                        searchEntity.StartTime,
                        searchEntity.EndTime))
            .OrderBy(r => r.StartTime)
            .ToList();

        if (searchEntity.RecordableOnly)
        {
            list = list
                .Where(r =>
                    r.EndTime > standardDateTimeOffset ||
                    r.AvailabilityTimeFree is AvailabilityTimeFree.Available or AvailabilityTimeFree.PartiallyAvailable)
                .ToList();
        }

        return list;
    }

    /// <summary>
    /// 古いradiko番組を削除する
    /// </summary>
    public async ValueTask DeleteOldRadikoProgramsAsync(DateOnly deleteDate, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var deletePrograms = await dbContext.RadikoPrograms
                .Where(r => r.RadioDate < deleteDate)
                .ToListAsync(cancellationToken);

            dbContext.RadikoPrograms.RemoveRange(deletePrograms);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// らじる★らじる番組一覧を日付/エリア/局で取得する
    /// </summary>
    public async ValueTask<List<NhkRadiruProgram>> GetRadiruProgramsAsync(DateOnly date, string areaId, string stationId, CancellationToken cancellationToken = default)
    {
        return await dbContext.NhkRadiruPrograms
            .Where(r => r.RadioDate == date)
            .Where(r => r.AreaId == areaId)
            .Where(r => r.StationId == stationId)
            .OrderBy(r => r.StartTime)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// らじる★らじる番組をIDで取得する
    /// </summary>
    public async ValueTask<NhkRadiruProgram?> GetRadiruProgramByIdAsync(string programId, CancellationToken cancellationToken = default)
    {
        return await dbContext.NhkRadiruPrograms.FindAsync([programId], cancellationToken);
    }

    /// <summary>
    /// らじる★らじる番組を追加または更新する
    /// </summary>
    public async ValueTask UpsertRadiruProgramsAsync(IEnumerable<NhkRadiruProgram> programs, CancellationToken cancellationToken = default)
    {
        var programList = programs
            .GroupBy(x => CreateRadiruProgramKey(x.AreaId, x.StationId, x.ProgramId))
            .Select(g => g.Last())
            .ToList();
        if (programList.Count == 0)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // 既存DBの主キーはProgramId単独。別エリア・局への再利用は更新前に検出する。
            var inputCollisions = programList.GroupBy(x => x.ProgramId, StringComparer.Ordinal)
                .Where(x => x.Select(p => (p.AreaId, p.StationId)).Distinct().Count() > 1)
                .Select(x => x.Key).ToList();
            var programIdsToCheck = programList.Select(x => x.ProgramId).ToList();
            var storedKeys = await dbContext.NhkRadiruPrograms.AsNoTracking()
                .Where(x => programIdsToCheck.Contains(x.ProgramId))
                .Select(x => new { x.ProgramId, x.AreaId, x.StationId }).ToListAsync(cancellationToken);
            var storedById = storedKeys.ToDictionary(x => x.ProgramId, StringComparer.Ordinal);
            var storedCollisions = programList.Where(x => storedById.TryGetValue(x.ProgramId, out var previous) &&
                (previous.AreaId != x.AreaId || previous.StationId != x.StationId)).Select(x => x.ProgramId);
            var collisions = inputCollisions.Concat(storedCollisions).Distinct(StringComparer.Ordinal).ToList();
            if (collisions.Count > 0)
            {
                throw new RadiCorder.Logics.Errors.DomainException($"らじる番組IDが異なるエリア・局で重複しています。既存DBを保持します。対象: {string.Join(",", collisions.Take(5))}");
            }

            var existingPrograms = new HashSet<string>(StringComparer.Ordinal);
            var trackedProgramsByKey = dbContext.NhkRadiruPrograms.Local
                .ToDictionary(
                    x => CreateRadiruProgramKey(x.AreaId, x.StationId, x.ProgramId),
                    StringComparer.Ordinal);

            foreach (var group in programList.GroupBy(x => new { x.AreaId, x.StationId }))
            {
                var areaId = group.Key.AreaId;
                var stationId = group.Key.StationId;
                var programIds = group.Select(x => x.ProgramId).Distinct().ToList();

                var matchedProgramIds = await dbContext.NhkRadiruPrograms
                    .AsNoTracking()
                    .Where(r => r.AreaId == areaId)
                    .Where(r => r.StationId == stationId)
                    .Where(r => programIds.Contains(r.ProgramId))
                    .Select(r => r.ProgramId)
                    .ToListAsync(cancellationToken);

                foreach (var matchedProgramId in matchedProgramIds)
                {
                    existingPrograms.Add(CreateRadiruProgramKey(areaId, stationId, matchedProgramId));
                }
            }

            foreach (var program in programList)
            {
                var programKey = CreateRadiruProgramKey(program.AreaId, program.StationId, program.ProgramId);

                if (existingPrograms.Contains(programKey))
                {
                    if (trackedProgramsByKey.TryGetValue(programKey, out var trackedProgram))
                    {
                        dbContext.Entry(trackedProgram).CurrentValues.SetValues(program);
                    }
                    else
                    {
                        dbContext.NhkRadiruPrograms.Attach(program);
                        dbContext.Entry(program).State = EntityState.Modified;
                        trackedProgramsByKey[programKey] = program;
                    }
                    continue;
                }

                await dbContext.NhkRadiruPrograms.AddAsync(program, cancellationToken);
                trackedProgramsByKey[programKey] = program;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            DetachTrackedEntities<NhkRadiruProgram>();
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            DetachTrackedEntities<NhkRadiruProgram>();
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// らじる★らじる番組を検索する
    /// </summary>
    public async ValueTask<List<NhkRadiruProgram>> SearchRadiruProgramsAsync(
        ProgramSearchEntity searchEntity,
        DateTimeOffset standardDateTimeOffset,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.NhkRadiruPrograms
            .AsNoTracking()
            .AsQueryable();

        if (searchEntity.SelectedRadiruStationIds.Count != 0)
        {
            query = query.Where(p => searchEntity.SelectedRadiruStationIds.Contains(p.AreaId + ":" + p.StationId));
        }

        if (!string.IsNullOrWhiteSpace(searchEntity.Keyword))
        {
            var keywords = searchEntity.Keyword.ParseKeywords();

            if (searchEntity.SearchTitleOnly)
            {
                query = query.Where(p => keywords.All(keyword => p.Title.Contains(keyword) || p.Subtitle.Contains(keyword)));
            }
            else
            {
                query = query.Where(
                    p =>
                        keywords.All(keyword =>
                            p.Title.Contains(keyword) ||
                            p.Subtitle.Contains(keyword) ||
                            p.Performer.Contains(keyword) ||
                            p.Description.Contains(keyword))
                );
            }
        }

        if (!string.IsNullOrWhiteSpace(searchEntity.ExcludedKeyword))
        {
            var excludedKeywords = searchEntity.ExcludedKeyword.ParseKeywords();

            if (searchEntity.SearchTitleOnlyExcludedKeyword)
            {
                query = query.Where(p => !excludedKeywords.Any(excluded => p.Title.Contains(excluded) || p.Subtitle.Contains(excluded)));
            }
            else
            {
                query = query.Where(p =>
                    !excludedKeywords.Any(excluded =>
                        p.Title.Contains(excluded) ||
                        p.Subtitle.Contains(excluded) ||
                        p.Performer.Contains(excluded) ||
                        p.Description.Contains(excluded))
                );
            }
        }

        if (searchEntity.SelectedDaysOfWeek.Count != 0)
        {
            var selectedDays = searchEntity.SelectedDaysOfWeek.Aggregate(DaysOfWeek.None, (acc, day) => acc | day);
            query = query.Where(p => (p.DaysOfWeek & selectedDays) != DaysOfWeek.None);
        }

        var limitRadioDate = standardDateTimeOffset.AddDays(-7).ToRadioDate();

        // DateTimeOffset のSQL比較はSQLiteで期待どおりにならない場合があるため、
        // 終了済み判定はアプリ側で評価する。
        var list = (await query
                .Where(r => r.RadioDate >= limitRadioDate)
                .ToListAsync(cancellationToken))
            .Where(
                r =>
                    (searchEntity.IncludeHistoricalPrograms || r.EndTime >= standardDateTimeOffset) &&
                    IsProgramWithinSearchTimeRange(
                        r.StartTime,
                        r.EndTime,
                        searchEntity.StartTime,
                        searchEntity.EndTime))
            .OrderBy(r => r.StartTime)
            .ToList();

        if (searchEntity.RecordableOnly)
        {
            list = list
                .Where(r =>
                    r.EndTime > standardDateTimeOffset ||
                    (!string.IsNullOrWhiteSpace(r.OnDemandContentUrl) &&
                     r.OnDemandExpiresAtUtc.HasValue &&
                     r.OnDemandExpiresAtUtc.Value > standardDateTimeOffset.UtcDateTime))
                .ToList();
        }

        return list;
    }

    /// <summary>
    /// 古いらじる★らじる番組を削除する
    /// </summary>
    public async ValueTask DeleteOldRadiruProgramsAsync(DateOnly deleteDate, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var deletePrograms = await dbContext.NhkRadiruPrograms
                .Where(r => r.RadioDate < deleteDate)
                .ToListAsync(cancellationToken);

            dbContext.NhkRadiruPrograms.RemoveRange(deletePrograms);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 番組表の最終更新日時を取得する
    /// </summary>
    public async ValueTask<DateTimeOffset?> GetLastUpdatedProgramAsync(CancellationToken cancellationToken = default)
    {
        var config = await dbContext.AppConfigurations
            .Where(r => r.ConfigurationName == AppConfigurationNames.LastUpdatedProgram)
            .FirstOrDefaultAsync(cancellationToken);

        return config?.Val4;
    }

    /// <summary>
    /// 番組表の最終更新日時を更新する
    /// </summary>
    public async ValueTask SetLastUpdatedProgramAsync(DateTimeOffset dateTime, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var config = await dbContext.AppConfigurations
                .Where(r => r.ConfigurationName == AppConfigurationNames.LastUpdatedProgram)
                .FirstOrDefaultAsync(cancellationToken);

            if (config == null)
            {
                config = new AppConfiguration
                {
                    ConfigurationName = AppConfigurationNames.LastUpdatedProgram,
                    Val4 = dateTime.UtcDateTime
                };

                await dbContext.AppConfigurations.AddAsync(config, cancellationToken);
            }
            else
            {
                config.Val4 = dateTime.UtcDateTime;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// スケジュール済みジョブ一覧を取得する
    /// </summary>
    public async ValueTask<List<ScheduleJob>> GetScheduleJobsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ScheduleJob
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 指定したスケジュールジョブを無効化する
    /// </summary>
    public async ValueTask<bool> DisableScheduleJobAsync(Ulid jobId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var job = await dbContext.ScheduleJob.FindAsync([jobId], cancellationToken);
            if (job == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            if (!job.IsEnabled)
            {
                await transaction.CommitAsync(cancellationToken);
                return true;
            }

            job.IsEnabled = false;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool IsProgramWithinSearchTimeRange(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        TimeOnly searchStart,
        TimeOnly searchEnd)
    {
        // 1日全体を対象にする既定条件では、日跨ぎ番組も含めて常に一致とする。
        if (searchStart == TimeOnly.MinValue && searchEnd == new TimeOnly(23, 59))
        {
            return true;
        }

        var day = TimeSpan.FromDays(1);
        var localStart = TimeZoneInfo.ConvertTime(startUtc, JapanStandardTimeZone).TimeOfDay;
        var localEnd = TimeZoneInfo.ConvertTime(endUtc, JapanStandardTimeZone).TimeOfDay;
        if (localEnd <= localStart)
        {
            localEnd += day;
        }

        var searchStartSpan = searchStart.ToTimeSpan();
        var searchEndSpan = searchEnd.ToTimeSpan();
        if (searchEndSpan <= searchStartSpan)
        {
            searchEndSpan += day;
        }

        var programRanges = new[]
        {
            (Start: localStart, End: localEnd),
            (Start: localStart + day, End: localEnd + day)
        };
        var searchRanges = new[]
        {
            (Start: searchStartSpan, End: searchEndSpan),
            (Start: searchStartSpan + day, End: searchEndSpan + day)
        };

        return programRanges.Any(programRange =>
            searchRanges.Any(searchRange =>
                programRange.Start >= searchRange.Start &&
                programRange.End <= searchRange.End));
    }

    private void DetachTrackedEntities<TEntity>()
        where TEntity : class
    {
        var entries = dbContext.ChangeTracker
            .Entries<TEntity>()
            .ToList();

        foreach (var entry in entries)
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string CreateRadiruProgramKey(string areaId, string stationId, string programId)
    {
        return $"{areaId}\t{stationId}\t{programId}";
    }
}
