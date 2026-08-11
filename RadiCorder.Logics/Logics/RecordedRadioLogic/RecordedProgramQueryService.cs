using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic;

/// <summary>
/// 録音済み番組の検索・参照を担当するサービス
/// </summary>
public class RecordedProgramQueryService(
    ILogger<RecordedProgramQueryService> logger,
    IEntryMapper entryMapper,
    TagLobLogic tagLobLogic,
    IAppConfigurationService config,
    RadioDbContext dbContext)
{
    /// <summary>
    /// 録音済み番組のリストを取得する
    /// </summary>
    /// <param name="searchQuery">検索クエリ</param>
    /// <param name="page">ページ番号</param>
    /// <param name="pageSize">ページサイズ</param>
    /// <param name="sortBy">ソートキー</param>
    /// <param name="isDescending">降順かどうか</param>
    /// <param name="withinDays"></param>
    /// <param name="stationId"></param>
    /// <param name="tagIds"></param>
    /// <param name="tagMode"></param>
    /// <param name="untaggedOnly"></param>
    /// <param name="unlistenedOnly"></param>
    public async ValueTask<(bool IsSuccess, int Total, List<RecordedProgramEntry>? List, Exception? Error)> GetRecorderProgramListAsync(
        string searchQuery,
        int page,
        int pageSize,
        string sortBy,
        bool isDescending,
        int? withinDays,
        string stationId,
        List<Guid>? tagIds = null,
        string tagMode = "or",
        bool untaggedOnly = false,
        bool unlistenedOnly = false)
    {
        try
        {
            var normalizedSearchQuery = searchQuery?.Trim();
            var query =
                from r in dbContext.Recordings.AsNoTracking()
                join m in dbContext.RecordingMetadatas.AsNoTracking() on r.Id equals m.RecordingId
                join f in dbContext.RecordingFiles.AsNoTracking() on r.Id equals f.RecordingId
                where r.State == RecordingState.Completed
                select new { Recording = r, Metadata = m, File = f };

            if (!string.IsNullOrWhiteSpace(normalizedSearchQuery))
            {
                var escapedSearchQuery = EscapeLikePattern(normalizedSearchQuery);
                query = query.Where(x => EF.Functions.Like(x.Metadata.Title, $"%{escapedSearchQuery}%", @"\"));
            }

            if (!string.IsNullOrWhiteSpace(stationId))
            {
                query = query.Where(x => x.Recording.StationId == stationId);
            }

            if (withinDays.HasValue && withinDays.Value > 0)
            {
                var from = DateTimeOffset.UtcNow.AddDays(-withinDays.Value);
                query = query.Where(x => x.Recording.EndDateTime >= from);
            }

            if (unlistenedOnly)
            {
                query = query.Where(x => !x.Recording.IsListened);
            }

            var normalizedTagIds = (tagIds ?? []).Distinct().ToList();
            if (untaggedOnly)
            {
                query = query.Where(x => !dbContext.RecordingTagRelations.Any(rt => rt.RecordingId == x.Recording.Id));
            }
            else if (normalizedTagIds.Count > 0)
            {
                if (tagMode.Equals("and", StringComparison.OrdinalIgnoreCase))
                {
                    var requiredCount = normalizedTagIds.Count;
                    query = query.Where(x =>
                        dbContext.RecordingTagRelations
                            .Where(rt => rt.RecordingId == x.Recording.Id && normalizedTagIds.Contains(rt.TagId))
                            .Select(rt => rt.TagId)
                            .Distinct()
                            .Count() == requiredCount);
                }
                else
                {
                    query = query.Where(x => dbContext.RecordingTagRelations.Any(rt => rt.RecordingId == x.Recording.Id && normalizedTagIds.Contains(rt.TagId)));
                }
            }

            var totalRecords = await query.CountAsync();

            // ソート
            if (sortBy == "Duration")
            {
                // SQLiteではDurationの差分計算が式変換されにくいため、メモリ上で並び替える
                var all = await query.ToListAsync();
                var list = (isDescending
                        ? all.OrderByDescending(r => (r.Recording.EndDateTime - r.Recording.StartDateTime).TotalSeconds)
                        : all.OrderBy(r => (r.Recording.EndDateTime - r.Recording.StartDateTime).TotalSeconds))
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                var results = list
                    .Select(x => entryMapper.ToRecordedProgramEntry(x.Recording, x.Metadata, x.File))
                    .ToList();

                await AttachTagsAsync(results);
                return (true, totalRecords, results, null);
            }

            query = sortBy switch
            {
                "Title" => isDescending ? query.OrderByDescending(r => r.Metadata.Title) : query.OrderBy(r => r.Metadata.Title),
                "StartDateTime" => isDescending ? query.OrderByDescending(r => r.Recording.StartDateTime) : query.OrderBy(r => r.Recording.StartDateTime),
                "EndDateTime" => isDescending ? query.OrderByDescending(r => r.Recording.EndDateTime) : query.OrderBy(r => r.Recording.EndDateTime),
                _ => query.OrderBy(r => r.Recording.StartDateTime),
            };

            {
                var list = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var results = list
                    .Select(x => entryMapper.ToRecordedProgramEntry(x.Recording, x.Metadata, x.File))
                    .ToList();

                await AttachTagsAsync(results);
                return (true, totalRecords, results, null);
            }
        }
        catch (Exception e)
        {
            logger.ZLogError(e, $"録音済み番組の取得に失敗しました。", e);
            return (false, 0, null, e);
        }
    }

    /// <summary>
    /// 録音一覧へタグを付与する
    /// </summary>
    private async ValueTask AttachTagsAsync(List<RecordedProgramEntry> results)
    {
        var map = await tagLobLogic.GetRecordingTagMapAsync(results.Select(r => r.Id));
        foreach (var entry in results)
        {
            if (map.TryGetValue(entry.Id, out var tags))
            {
                entry.Tags = tags;
            }
        }
    }

    /// <summary>
    /// 録音済み番組一覧の放送局フィルタ候補を取得する
    /// </summary>
    public async ValueTask<(bool IsSuccess, List<RecordedStationFilterEntry>? List, Exception? Error)> GetRecordedStationFiltersAsync()
    {
        try
        {
            var stations = await dbContext.Recordings
                .AsNoTracking()
                .Where(r => r.State == RecordingState.Completed)
                .Join(
                    dbContext.RecordingMetadatas.AsNoTracking(),
                    r => r.Id,
                    m => m.RecordingId,
                    (r, m) => new { r.ServiceKind, r.StationId, m.StationName })
                .Distinct()
                .ToListAsync();

            var list = stations
                .Where(x => !string.IsNullOrWhiteSpace(x.StationId))
                .Select(x => new RecordedStationFilterEntry
                {
                    StationId = x.StationId,
                    StationName = x.ServiceKind == RadioServiceKind.Other
                        ? (string.IsNullOrWhiteSpace(x.StationName) ? "不明" : x.StationName)
                        : config.ChooseStationName(x.ServiceKind, x.StationId)
                })
                .OrderBy(x => x.StationName)
                .ToList();

            return (true, list, null);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"録音済み番組の放送局フィルタ候補取得に失敗しました。");
            return (false, null, ex);
        }
    }

    /// <summary>
    /// 指定された録音済み番組が存在するかどうかを確認する
    /// </summary>
    public async ValueTask<(bool IsSuccess, bool IsExists)> CheckProgramExistsAsync(Ulid recorderId)
    {
        var exists = await dbContext.Recordings.AnyAsync(r => r.Id == recorderId);
        return (true, exists);
    }

    /// <summary>
    /// 録音済み番組を視聴済みに更新する
    /// </summary>
    public async ValueTask MarkAsListenedAsync(Ulid recorderId)
    {
        var recording = await dbContext.Recordings.FindAsync(recorderId);
        if (recording == null || recording.IsListened)
        {
            return;
        }

        recording.IsListened = true;
        recording.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 録音済み番組を一括で既読/未読に更新する
    /// </summary>
    public async ValueTask<(int SuccessCount, int SkipCount, int FailCount, List<string> FailedRecordingIds, List<string> SkippedRecordingIds)> BulkUpdateListenedStateAsync(
        IReadOnlyCollection<Ulid> recordingIds,
        bool isListened)
    {
        if (recordingIds.Count == 0)
        {
            return (0, 0, 0, [], []);
        }

        try
        {
            var targetIds = recordingIds.Distinct().ToList();
            var recordings = await dbContext.Recordings
                .Where(x => targetIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id);

            var successCount = 0;
            var skipCount = 0;
            var failCount = 0;
            var failedRecordingIds = new List<string>();
            var skippedRecordingIds = new List<string>();
            var updatedAt = DateTimeOffset.UtcNow;

            foreach (var id in targetIds)
            {
                if (!recordings.TryGetValue(id, out var recording))
                {
                    skipCount++;
                    skippedRecordingIds.Add(id.ToString());
                    continue;
                }

                if (recording.IsListened == isListened)
                {
                    skipCount++;
                    skippedRecordingIds.Add(id.ToString());
                    continue;
                }

                recording.IsListened = isListened;
                recording.UpdatedAt = updatedAt;
                successCount++;
            }

            if (successCount > 0)
            {
                await dbContext.SaveChangesAsync();
            }

            return (successCount, skipCount, failCount, failedRecordingIds, skippedRecordingIds);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"録音済み番組の一括既読/未読更新に失敗しました。");
            return (0, 0, recordingIds.Count, recordingIds.Select(x => x.ToString()).ToList(), []);
        }
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
    }

}
