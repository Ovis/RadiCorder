using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Logics.TagLogic;

/// <summary>
/// タグ管理ロジック
/// </summary>
public class TagLobLogic(
    ILogger<TagLobLogic> logger,
    RadioDbContext dbContext)
{
    private const int TagNameMaxLength = 64;

    /// <summary>
    /// タグ一覧取得
    /// </summary>
    public async ValueTask<List<TagEntry>> GetTagsAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword.NormalizeTagName();

        var query = dbContext.RecordingTags.AsNoTracking();
        if (!string.IsNullOrEmpty(normalizedKeyword))
        {
            query = query.Where(t => t.NormalizedName.Contains(normalizedKeyword));
        }

        var list = await query
            .Select(t => new TagEntry
            {
                Id = t.Id,
                Name = t.Name,
                RecordingCount = t.RecordingTagRelations.Count,
                LastUsedAt = t.LastUsedAt,
                CreatedAt = t.CreatedAt
            })
            .OrderByDescending(t => t.LastUsedAt ?? DateTimeOffset.MinValue)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

        return list;
    }

    /// <summary>
    /// 指定IDのタグ名一覧を取得
    /// </summary>
    public async ValueTask<List<string>> GetTagNamesByIdsAsync(IEnumerable<Guid> tagIds, CancellationToken cancellationToken = default)
    {
        var idList = tagIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        return await dbContext.RecordingTags
            .AsNoTracking()
            .Where(t => idList.Contains(t.Id))
            .OrderBy(t => t.Name)
            .Select(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// キーワード予約IDごとのタグ名一覧を取得
    /// </summary>
    public async ValueTask<Dictionary<Ulid, List<string>>> GetKeywordReserveTagNameMapAsync(
        IEnumerable<Ulid> reserveIds,
        CancellationToken cancellationToken = default)
    {
        var idList = reserveIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.KeywordReserveTagRelations
            .AsNoTracking()
            .Where(r => idList.Contains(r.ReserveId))
            .Join(
                dbContext.RecordingTags.AsNoTracking(),
                relation => relation.TagId,
                tag => tag.Id,
                (relation, tag) => new { relation.ReserveId, tag.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ReserveId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Name).Distinct().OrderBy(x => x).ToList());
    }

    /// <summary>
    /// タグ作成
    /// </summary>
    public async ValueTask<TagEntry> CreateTagAsync(string name, CancellationToken cancellationToken = default)
    {
        var (displayName, normalizedName) = ValidateAndNormalize(name);

        var exists = await dbContext.RecordingTags
            .AnyAsync(t => t.NormalizedName == normalizedName, cancellationToken);
        if (exists)
        {
            throw new DomainException("同じタグ名が既に存在します。");
        }

        var now = DateTimeOffset.UtcNow;
        var tag = new RecordingTag
        {
            Id = Guid.NewGuid(),
            Name = displayName,
            NormalizedName = normalizedName,
            CreatedAt = now,
            UpdatedAt = now
        };

        await dbContext.RecordingTags.AddAsync(tag, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TagEntry
        {
            Id = tag.Id,
            Name = tag.Name,
            RecordingCount = 0,
            LastUsedAt = null,
            CreatedAt = tag.CreatedAt
        };
    }

    /// <summary>
    /// タグ更新
    /// </summary>
    public async ValueTask<TagEntry> UpdateTagAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var tag = await dbContext.RecordingTags
            .Include(t => t.RecordingTagRelations)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tag == null)
        {
            throw new DomainException("指定されたタグが見つかりません。");
        }

        var (displayName, normalizedName) = ValidateAndNormalize(name);
        var duplicate = await dbContext.RecordingTags
            .AnyAsync(t => t.Id != id && t.NormalizedName == normalizedName, cancellationToken);
        if (duplicate)
        {
            throw new DomainException("同じタグ名が既に存在します。");
        }

        tag.Name = displayName;
        tag.NormalizedName = normalizedName;
        tag.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TagEntry
        {
            Id = tag.Id,
            Name = tag.Name,
            RecordingCount = tag.RecordingTagRelations.Count,
            LastUsedAt = tag.LastUsedAt,
            CreatedAt = tag.CreatedAt
        };
    }

    /// <summary>
    /// タグ削除
    /// </summary>
    public async ValueTask DeleteTagAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tag = await dbContext.RecordingTags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tag == null)
        {
            return;
        }

        dbContext.RecordingTags.Remove(tag);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// タグ統合
    /// </summary>
    public async ValueTask MergeTagAsync(Guid fromTagId, Guid toTagId, CancellationToken cancellationToken = default)
    {
        if (fromTagId == toTagId)
        {
            throw new DomainException("統合元と統合先が同じです。");
        }

        var fromTag = await dbContext.RecordingTags.FirstOrDefaultAsync(t => t.Id == fromTagId, cancellationToken);
        var toTag = await dbContext.RecordingTags.FirstOrDefaultAsync(t => t.Id == toTagId, cancellationToken);
        if (fromTag == null || toTag == null)
        {
            throw new DomainException("統合対象のタグが見つかりません。");
        }

        await using var tran = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var recordingRelations = await dbContext.RecordingTagRelations
                .Where(r => r.TagId == fromTagId)
                .ToListAsync(cancellationToken);

            foreach (var relation in recordingRelations)
            {
                var exists = await dbContext.RecordingTagRelations
                    .AnyAsync(r => r.RecordingId == relation.RecordingId && r.TagId == toTagId, cancellationToken);
                if (!exists)
                {
                    dbContext.RecordingTagRelations.Remove(relation);
                    await dbContext.RecordingTagRelations.AddAsync(new RecordingTagRelation
                    {
                        RecordingId = relation.RecordingId,
                        TagId = toTagId
                    }, cancellationToken);
                }
                else
                {
                    dbContext.RecordingTagRelations.Remove(relation);
                }
            }

            var reserveRelations = await dbContext.KeywordReserveTagRelations
                .Where(r => r.TagId == fromTagId)
                .ToListAsync(cancellationToken);

            foreach (var relation in reserveRelations)
            {
                var exists = await dbContext.KeywordReserveTagRelations
                    .AnyAsync(r => r.ReserveId == relation.ReserveId && r.TagId == toTagId, cancellationToken);
                if (!exists)
                {
                    dbContext.KeywordReserveTagRelations.Remove(relation);
                    await dbContext.KeywordReserveTagRelations.AddAsync(new KeywordReserveTagRelation
                    {
                        ReserveId = relation.ReserveId,
                        TagId = toTagId
                    }, cancellationToken);
                }
                else
                {
                    dbContext.KeywordReserveTagRelations.Remove(relation);
                }
            }

            toTag.LastUsedAt = Max(toTag.LastUsedAt, fromTag.LastUsedAt);
            toTag.UpdatedAt = DateTimeOffset.UtcNow;

            dbContext.RecordingTags.Remove(fromTag);
            await dbContext.SaveChangesAsync(cancellationToken);
            await tran.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tran.RollbackAsync(cancellationToken);
            logger.ZLogError(ex, $"タグ統合に失敗しました。");
            throw;
        }
    }

    /// <summary>
    /// 録音IDごとのタグ名一覧を取得
    /// </summary>
    public async ValueTask<Dictionary<Ulid, List<string>>> GetRecordingTagMapAsync(IEnumerable<Ulid> recordingIds, CancellationToken cancellationToken = default)
    {
        var idList = recordingIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        var rows = await dbContext.RecordingTagRelations
            .AsNoTracking()
            .Where(r => idList.Contains(r.RecordingId))
            .Join(
                dbContext.RecordingTags.AsNoTracking(),
                relation => relation.TagId,
                tag => tag.Id,
                (relation, tag) => new { relation.RecordingId, tag.Name })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.RecordingId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name).OrderBy(x => x).ToList());
    }

    /// <summary>
    /// 録音にタグを付与
    /// </summary>
    public async ValueTask AddTagsToRecordingAsync(Ulid recordingId, IEnumerable<Guid> tagIds, CancellationToken cancellationToken = default)
    {
        var normalizedTagIds = tagIds.Distinct().ToList();
        if (normalizedTagIds.Count == 0)
        {
            return;
        }

        var exists = await dbContext.Recordings.AnyAsync(r => r.Id == recordingId, cancellationToken);
        if (!exists)
        {
            throw new DomainException("対象の録音が見つかりません。");
        }

        var tags = await dbContext.RecordingTags
            .Where(t => normalizedTagIds.Contains(t.Id))
            .ToListAsync(cancellationToken);

        var missing = normalizedTagIds.Except(tags.Select(t => t.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException("存在しないタグが指定されました。");
        }

        var now = DateTimeOffset.UtcNow;
        var existingMap = await dbContext.RecordingTagRelations
            .Where(r => r.RecordingId == recordingId && normalizedTagIds.Contains(r.TagId))
            .Select(r => r.TagId)
            .ToListAsync(cancellationToken);

        var addIds = normalizedTagIds.Except(existingMap).ToList();
        if (addIds.Count > 0)
        {
            var rows = addIds.Select(tagId => new RecordingTagRelation
            {
                RecordingId = recordingId,
                TagId = tagId
            });

            await dbContext.RecordingTagRelations.AddRangeAsync(rows, cancellationToken);
        }

        foreach (var tag in tags)
        {
            tag.LastUsedAt = now;
            tag.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 録音からタグを解除
    /// </summary>
    public async ValueTask RemoveTagFromRecordingAsync(Ulid recordingId, Guid tagId, CancellationToken cancellationToken = default)
    {
        var relation = await dbContext.RecordingTagRelations
            .FirstOrDefaultAsync(r => r.RecordingId == recordingId && r.TagId == tagId, cancellationToken);
        if (relation == null)
        {
            return;
        }

        dbContext.RecordingTagRelations.Remove(relation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 録音へのタグ一括付与
    /// </summary>
    public async ValueTask<TagBulkOperationResult> BulkAddTagsToRecordingsAsync(
        IEnumerable<Ulid> recordingIds,
        IEnumerable<Guid> tagIds,
        CancellationToken cancellationToken = default)
    {
        var recordingIdList = recordingIds.Distinct().ToList();
        var tagIdList = tagIds.Distinct().ToList();
        ValidateBulkRequest(recordingIdList, tagIdList);

        var result = new TagBulkOperationResult();
        var failedIds = new List<string>();

        foreach (var recordingId in recordingIdList)
        {
            try
            {
                var before = await dbContext.RecordingTagRelations.CountAsync(r => r.RecordingId == recordingId, cancellationToken);
                await AddTagsToRecordingAsync(recordingId, tagIdList, cancellationToken);
                var after = await dbContext.RecordingTagRelations.CountAsync(r => r.RecordingId == recordingId, cancellationToken);
                if (after > before)
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.SkipCount++;
                }
            }
            catch (DomainException)
            {
                result.FailCount++;
                failedIds.Add(recordingId.ToString());
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"録音へのタグ一括付与で失敗しました。");
                result.FailCount++;
                failedIds.Add(recordingId.ToString());
            }
        }

        result.FailedRecordingIds = failedIds;
        return result;
    }

    /// <summary>
    /// 録音からタグ一括解除
    /// </summary>
    public async ValueTask<TagBulkOperationResult> BulkRemoveTagsFromRecordingsAsync(
        IEnumerable<Ulid> recordingIds,
        IEnumerable<Guid> tagIds,
        CancellationToken cancellationToken = default)
    {
        var recordingIdList = recordingIds.Distinct().ToList();
        var tagIdList = tagIds.Distinct().ToList();
        ValidateBulkRequest(recordingIdList, tagIdList);

        var result = new TagBulkOperationResult();
        var failedIds = new List<string>();

        foreach (var recordingId in recordingIdList)
        {
            try
            {
                var rows = await dbContext.RecordingTagRelations
                    .Where(r => r.RecordingId == recordingId && tagIdList.Contains(r.TagId))
                    .ToListAsync(cancellationToken);

                if (rows.Count == 0)
                {
                    result.SkipCount++;
                    continue;
                }

                dbContext.RecordingTagRelations.RemoveRange(rows);
                await dbContext.SaveChangesAsync(cancellationToken);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"録音からのタグ一括解除で失敗しました。");
                result.FailCount++;
                failedIds.Add(recordingId.ToString());
            }
        }

        result.FailedRecordingIds = failedIds;
        return result;
    }

    /// <summary>
    /// キーワード予約にタグを設定
    /// </summary>
    public async ValueTask SetKeywordReserveTagsAsync(Ulid reserveId, IEnumerable<Guid> tagIds, CancellationToken cancellationToken = default)
    {
        var normalizedTagIds = tagIds.Distinct().ToList();
        var now = DateTimeOffset.UtcNow;

        var exists = await dbContext.KeywordReserve.AnyAsync(r => r.Id == reserveId, cancellationToken);
        if (!exists)
        {
            throw new DomainException("対象のキーワード予約が見つかりません。");
        }

        if (normalizedTagIds.Count > 0)
        {
            var count = await dbContext.RecordingTags.CountAsync(t => normalizedTagIds.Contains(t.Id), cancellationToken);
            if (count != normalizedTagIds.Count)
            {
                throw new DomainException("存在しないタグが指定されました。");
            }
        }

        var existing = await dbContext.KeywordReserveTagRelations
            .Where(r => r.ReserveId == reserveId)
            .ToListAsync(cancellationToken);
        dbContext.KeywordReserveTagRelations.RemoveRange(existing);

        if (normalizedTagIds.Count > 0)
        {
            var addRows = normalizedTagIds.Select(tagId => new KeywordReserveTagRelation
            {
                ReserveId = reserveId,
                TagId = tagId
            });
            await dbContext.KeywordReserveTagRelations.AddRangeAsync(addRows, cancellationToken);

            var tags = await dbContext.RecordingTags
                .Where(t => normalizedTagIds.Contains(t.Id))
                .ToListAsync(cancellationToken);
            foreach (var tag in tags)
            {
                tag.LastUsedAt = now;
                tag.UpdatedAt = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// キーワード予約のタグID一覧を取得
    /// </summary>
    public async ValueTask<List<Guid>> GetKeywordReserveTagIdsAsync(Ulid reserveId, CancellationToken cancellationToken = default)
    {
        return await dbContext.KeywordReserveTagRelations
            .AsNoTracking()
            .Where(r => r.ReserveId == reserveId)
            .Select(r => r.TagId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// キーワード予約由来タグを録音に自動付与
    /// </summary>
    public async ValueTask AddKeywordReserveTagsToRecordingAsync(Ulid recordingId, Ulid keywordReserveId, CancellationToken cancellationToken = default)
    {
        var tagIds = await GetKeywordReserveTagIdsAsync(keywordReserveId, cancellationToken);
        if (tagIds.Count == 0)
        {
            return;
        }

        await AddTagsToRecordingAsync(recordingId, tagIds, cancellationToken);
    }

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a == null)
        {
            return b;
        }

        if (b == null)
        {
            return a;
        }

        return a.Value >= b.Value ? a : b;
    }

    private static void ValidateBulkRequest(List<Ulid> recordingIds, List<Guid> tagIds)
    {
        if (recordingIds.Count == 0)
        {
            throw new DomainException("録音が選択されていません。");
        }

        if (tagIds.Count == 0)
        {
            throw new DomainException("タグが選択されていません。");
        }
    }

    private static (string DisplayName, string NormalizedName) ValidateAndNormalize(string name)
    {
        var displayName = (name ?? string.Empty).Trim();
        var normalizedName = displayName.NormalizeTagName();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new DomainException("タグ名を入力してください。");
        }

        if (displayName.Length > TagNameMaxLength)
        {
            throw new DomainException($"タグ名は{TagNameMaxLength}文字以内で入力してください。");
        }

        return (displayName, normalizedName);
    }
}
