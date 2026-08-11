using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// お知らせリポジトリのテスト用スタブ
/// </summary>
public class FakeNotificationRepository : INotificationRepository
{
    private readonly List<Notification> _store = [];

    public ValueTask<int> GetUnreadCountAsync(
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(FilterUnread(categories).Count());

    public ValueTask<List<Notification>> GetUnreadListAsync(
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(FilterUnread(categories).ToList());

    public ValueTask MarkReadBeforeAsync(
        DateTimeOffset dateTime,
        IEnumerable<NoticeCategory>? categories = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var item in FilterUnread(categories).Where(n => n.Timestamp <= dateTime.UtcDateTime))
        {
            item.IsRead = true;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<(int Total, List<Notification> List)> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var list = _store.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return ValueTask.FromResult((_store.Count, list));
    }

    public ValueTask AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _store.Add(notification);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        _store.Clear();
        return ValueTask.CompletedTask;
    }

    private IEnumerable<Notification> FilterUnread(IEnumerable<NoticeCategory>? categories)
    {
        var query = _store.Where(n => !n.IsRead);
        var categoryList = categories?.Distinct().ToList();
        if (categoryList is { Count: > 0 })
        {
            query = query.Where(n => categoryList.Contains(n.Category));
        }

        return query;
    }
}
