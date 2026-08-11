using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Infrastructure.Notification;

/// <summary>
/// お知らせの永続化を担うリポジトリ実装
/// </summary>
public class NotificationRepository(RadioDbContext dbContext) : INotificationRepository
{
    /// <summary>
    /// 未読のお知らせ件数を取得する
    /// </summary>
    public async ValueTask<int> GetUnreadCountAsync(
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Notification.Where(x => !x.IsRead);
        var categoryList = categories?.Distinct().ToList();
        if (categoryList is { Count: > 0 })
        {
            query = query.Where(x => categoryList.Contains(x.Category));
        }

        return await query.CountAsync(cancellationToken);
    }

    /// <summary>
    /// 未読のお知らせ一覧を取得する
    /// </summary>
    public async ValueTask<List<RdbContext.Notification>> GetUnreadListAsync(
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Notification
            .AsNoTracking()
            .Where(x => !x.IsRead);

        var categoryList = categories?.Distinct().ToList();
        if (categoryList is { Count: > 0 })
        {
            query = query.Where(x => categoryList.Contains(x.Category));
        }

        return await query.OrderByDescending(x => x.Timestamp).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 指定日時以前のお知らせを既読にする
    /// </summary>
    public async ValueTask MarkReadBeforeAsync(
        DateTimeOffset dateTime,
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default)
    {
        var utcThreshold = dateTime.ToUniversalTime();
        await using var dbTran = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var query = dbContext.Notification
                .Where(x => x.Timestamp <= utcThreshold)
                .Where(r => r.IsRead == false);

            var categoryList = categories?.Distinct().ToList();
            if (categoryList is { Count: > 0 })
            {
                query = query.Where(x => categoryList.Contains(x.Category));
            }

            var list = await query.ToListAsync(cancellationToken);

            foreach (var item in list)
            {
                item.IsRead = true;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTran.CommitAsync(cancellationToken);
        }
        catch
        {
            await dbTran.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// お知らせのページング一覧を取得する
    /// </summary>
    public async ValueTask<(int Total, List<RdbContext.Notification> List)> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Notification.AsQueryable();

        var totalRecords = await query.CountAsync(cancellationToken);

        var notificationList = await query
            .OrderByDescending(r => r.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (totalRecords, notificationList);
    }

    /// <summary>
    /// お知らせを登録する
    /// </summary>
    public async ValueTask AddAsync(RdbContext.Notification notification, CancellationToken cancellationToken = default)
    {
        await using var tran = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            await dbContext.Notification.AddAsync(notification, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await tran.CommitAsync(cancellationToken);
        }
        catch
        {
            await tran.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// お知らせを全件削除する
    /// </summary>
    public async ValueTask DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var dbTran = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.Database.ExecuteSqlRawAsync("DELETE FROM Notification", cancellationToken);
            await dbTran.CommitAsync(cancellationToken);
        }
        catch
        {
            await dbTran.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
