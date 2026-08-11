using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Infrastructure.Reserve;

/// <summary>
/// 予約関連の永続化を担うリポジトリ実装
/// </summary>
public class ReserveRepository(RadioDbContext dbContext) : IReserveRepository
{
    /// <summary>
    /// 録音予約一覧を取得する
    /// </summary>
    public async ValueTask<List<ScheduleJob>> GetScheduleJobsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext
            .ScheduleJob
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 録音予約を追加する
    /// </summary>
    public async ValueTask AddScheduleJobAsync(ScheduleJob job, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.ScheduleJob.AddAsync(job, cancellationToken);
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
    /// 録音予約を取得する
    /// </summary>
    public async ValueTask<ScheduleJob?> GetScheduleJobByIdAsync(Ulid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.ScheduleJob.FindAsync([id], cancellationToken);
    }

    /// <summary>
    /// 番組IDで録音予約を取得する
    /// </summary>
    public async ValueTask<ScheduleJob?> GetScheduleJobByProgramIdAsync(string programId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ScheduleJob
            .FirstOrDefaultAsync(x => x.ProgramId == programId, cancellationToken);
    }

    /// <summary>
    /// 録音予約を削除する
    /// </summary>
    public async ValueTask RemoveScheduleJobAsync(ScheduleJob job, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            dbContext.ScheduleJob.Remove(job);
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
    /// 録音予約を更新する
    /// </summary>
    public async ValueTask UpdateScheduleJobAsync(ScheduleJob job, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var tracked = dbContext.ScheduleJob.Local.FirstOrDefault(x => x.Id == job.Id);
            if (tracked != null && !ReferenceEquals(tracked, job))
            {
                dbContext.Entry(tracked).CurrentValues.SetValues(job);
            }
            else if (dbContext.Entry(job).State == EntityState.Detached)
            {
                dbContext.ScheduleJob.Attach(job);
                dbContext.Entry(job).State = EntityState.Modified;
            }
            else
            {
                dbContext.Entry(job).State = EntityState.Modified;
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
    /// マージン更新対象の予約を取得する
    /// </summary>
    public async ValueTask<List<ScheduleJob>> GetScheduleJobsNeedingDurationUpdateAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.ScheduleJob
            .Where(sj => sj.RecordingType == RecordingType.RealTime)
            .Where(sj => sj.StartDelay == null || sj.EndDelay == null)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 指定日時より古い予約を取得する
    /// </summary>
    public async ValueTask<List<ScheduleJob>> GetScheduleJobsOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        var utcThreshold = threshold.ToUniversalTime();
        return await dbContext.ScheduleJob
            .Where(sj => sj.EndDateTime < utcThreshold)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// キーワード予約一覧を取得する
    /// </summary>
    public async ValueTask<List<KeywordReserve>> GetKeywordReservesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.KeywordReserve
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 次に採番するキーワード予約の並び順を取得する
    /// </summary>
    public async ValueTask<int> GetNextKeywordReserveSortOrderAsync(CancellationToken cancellationToken = default)
    {
        var max = await dbContext.KeywordReserve
            .Select(x => (int?)x.SortOrder)
            .MaxAsync(cancellationToken);

        return (max ?? -1) + 1;
    }

    /// <summary>
    /// キーワード予約の放送局一覧を取得する
    /// </summary>
    public async ValueTask<List<KeywordReserveRadioStation>> GetKeywordReserveRadioStationsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.KeywordReserveRadioStations.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// キーワード予約を取得する
    /// </summary>
    public async ValueTask<KeywordReserve?> GetKeywordReserveByIdAsync(Ulid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.KeywordReserve.FindAsync([id], cancellationToken);
    }

    /// <summary>
    /// キーワード予約を追加する
    /// </summary>
    public async ValueTask AddKeywordReserveAsync(
        KeywordReserve reserve,
        IEnumerable<KeywordReserveRadioStation> stations,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.KeywordReserve.AddAsync(reserve, cancellationToken);
            await dbContext.KeywordReserveRadioStations.AddRangeAsync(stations, cancellationToken);
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
    /// キーワード予約を更新する
    /// </summary>
    public async ValueTask UpdateKeywordReserveAsync(
        KeywordReserve reserve,
        IEnumerable<KeywordReserveRadioStation> stations,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            dbContext.KeywordReserve.Update(reserve);
            await dbContext.KeywordReserveRadioStations.AddRangeAsync(stations, cancellationToken);
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
    /// キーワード予約の並び順を更新する
    /// </summary>
    public async ValueTask ReorderKeywordReservesAsync(IReadOnlyList<Ulid> orderedIds, CancellationToken cancellationToken = default)
    {
        if (orderedIds.Count == 0)
        {
            return;
        }

        var distinctIds = orderedIds.Distinct().ToList();
        var reserves = await dbContext.KeywordReserve
            .Where(x => distinctIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (reserves.Count != distinctIds.Count)
        {
            throw new InvalidOperationException("並び替え対象のキーワード予約が存在しません。");
        }

        var sortMap = orderedIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);

        foreach (var reserve in reserves)
        {
            reserve.SortOrder = sortMap[reserve.Id];
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
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
    /// キーワード予約を削除する
    /// </summary>
    public async ValueTask<bool> DeleteKeywordReserveAsync(Ulid id, CancellationToken cancellationToken = default)
    {
        var keywordReserve = await dbContext.KeywordReserve.FindAsync([id], cancellationToken);
        if (keywordReserve == null)
        {
            return false;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            dbContext.KeywordReserve.Remove(keywordReserve);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return true;
    }

    /// <summary>
    /// キーワード予約の放送局設定を削除する
    /// </summary>
    public async ValueTask DeleteKeywordReserveRadioStationsAsync(Ulid id, CancellationToken cancellationToken = default)
    {
        var stations = await dbContext.KeywordReserveRadioStations
            .Where(krs => krs.Id == id)
            .ToListAsync(cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            dbContext.KeywordReserveRadioStations.RemoveRange(stations);
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
    /// キーワード予約に紐づく録音予約を取得する
    /// </summary>
    public async ValueTask<List<ScheduleJob>> GetScheduleJobsByKeywordReserveIdAsync(Ulid id, CancellationToken cancellationToken = default)
    {
        var relationJobIds = await dbContext.ScheduleJobKeywordReserveRelations
            .Where(r => r.KeywordReserveId == id)
            .Select(r => r.ScheduleJobId)
            .ToListAsync(cancellationToken);

        return await dbContext.ScheduleJob
            .Where(sj => sj.KeywordReserveId == id || relationJobIds.Contains(sj.Id))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 番組IDで録音予約の存在を確認する
    /// </summary>
    public async ValueTask<bool> ExistsScheduleJobByProgramIdAsync(string programId, CancellationToken cancellationToken = default)
    {
        return await dbContext.ScheduleJob
            .AnyAsync(sj => sj.ProgramId == programId, cancellationToken);
    }

    /// <summary>
    /// 録音予約とキーワード予約の関連を一括追加する（重複は無視）
    /// </summary>
    public async ValueTask AddScheduleJobKeywordReserveRelationsAsync(
        IEnumerable<ScheduleJobKeywordReserveRelation> relations,
        CancellationToken cancellationToken = default)
    {
        var normalized = relations
            .DistinctBy(x => new { x.ScheduleJobId, x.KeywordReserveId })
            .ToList();

        if (normalized.Count == 0)
        {
            return;
        }

        var scheduleIds = normalized.Select(x => x.ScheduleJobId).Distinct().ToList();
        var reserveIds = normalized.Select(x => x.KeywordReserveId).Distinct().ToList();

        var existing = await dbContext.ScheduleJobKeywordReserveRelations
            .Where(x => scheduleIds.Contains(x.ScheduleJobId) && reserveIds.Contains(x.KeywordReserveId))
            .Select(x => new { x.ScheduleJobId, x.KeywordReserveId })
            .ToListAsync(cancellationToken);

        var existingSet = existing
            .Select(x => $"{x.ScheduleJobId}:{x.KeywordReserveId}")
            .ToHashSet();

        var rowsToAdd = normalized
            .Where(x => !existingSet.Contains($"{x.ScheduleJobId}:{x.KeywordReserveId}"))
            .ToList();

        if (rowsToAdd.Count == 0)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.ScheduleJobKeywordReserveRelations.AddRangeAsync(rowsToAdd, cancellationToken);
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
    /// 録音予約ごとの関連キーワード予約ID一覧を取得する
    /// </summary>
    public async ValueTask<Dictionary<Ulid, List<Ulid>>> GetKeywordReserveIdsByScheduleJobIdsAsync(
        IEnumerable<Ulid> scheduleJobIds,
        CancellationToken cancellationToken = default)
    {
        var jobIdList = scheduleJobIds.Distinct().ToList();
        if (jobIdList.Count == 0)
        {
            return [];
        }

        var scheduleKeywordRows = await dbContext.ScheduleJob
            .AsNoTracking()
            .Where(x => jobIdList.Contains(x.Id) && x.KeywordReserveId != null)
            .Select(x => new { x.Id, KeywordReserveId = x.KeywordReserveId!.Value })
            .ToListAsync(cancellationToken);

        var relationRows = await dbContext.ScheduleJobKeywordReserveRelations
            .AsNoTracking()
            .Where(x => jobIdList.Contains(x.ScheduleJobId))
            .Select(x => new { Id = x.ScheduleJobId, x.KeywordReserveId })
            .ToListAsync(cancellationToken);

        return scheduleKeywordRows
            .Concat(relationRows)
            .GroupBy(x => x.Id)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.KeywordReserveId).Distinct().ToList());
    }

    /// <summary>
    /// 指定したキーワード予約との関連を録音予約から解除する
    /// </summary>
    public async ValueTask RemoveKeywordReserveFromScheduleJobsAsync(
        Ulid keywordReserveId,
        IEnumerable<Ulid> scheduleJobIds,
        CancellationToken cancellationToken = default)
    {
        var jobIdList = scheduleJobIds.Distinct().ToList();
        if (jobIdList.Count == 0)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var scheduleKeywordJobs = await dbContext.ScheduleJob
                .Where(x => jobIdList.Contains(x.Id) && x.KeywordReserveId == keywordReserveId)
                .ToListAsync(cancellationToken);

            foreach (var job in scheduleKeywordJobs)
            {
                job.KeywordReserveId = null;
            }

            var relationRows = await dbContext.ScheduleJobKeywordReserveRelations
                .Where(x => jobIdList.Contains(x.ScheduleJobId) && x.KeywordReserveId == keywordReserveId)
                .ToListAsync(cancellationToken);

            dbContext.ScheduleJobKeywordReserveRelations.RemoveRange(relationRows);
            await dbContext.SaveChangesAsync(cancellationToken);

            var firstRelationRows = await dbContext.ScheduleJobKeywordReserveRelations
                .Where(x => jobIdList.Contains(x.ScheduleJobId))
                .Select(x => new
                {
                    x.ScheduleJobId,
                    x.KeywordReserveId,
                    x.KeywordReserve.SortOrder
                })
                .ToListAsync(cancellationToken);

            var firstRelationDict = firstRelationRows
                .GroupBy(x => x.ScheduleJobId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(x => x.SortOrder).ThenBy(x => x.KeywordReserveId).First().KeywordReserveId);

            foreach (var job in scheduleKeywordJobs.Where(x => x.KeywordReserveId == null))
            {
                if (firstRelationDict.TryGetValue(job.Id, out var fallbackKeywordReserveId))
                {
                    job.KeywordReserveId = fallbackKeywordReserveId;
                }
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
    /// 録音予約を一括削除する
    /// </summary>
    public async ValueTask RemoveScheduleJobsAsync(IEnumerable<ScheduleJob> jobs, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            dbContext.ScheduleJob.RemoveRange(jobs);
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
    /// 録音予約を一括追加する
    /// </summary>
    public async ValueTask AddScheduleJobsAsync(IEnumerable<ScheduleJob> jobs, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.ScheduleJob.AddRangeAsync(jobs, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
