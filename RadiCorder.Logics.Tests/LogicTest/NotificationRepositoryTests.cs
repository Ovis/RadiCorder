using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Infrastructure.Notification;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// NotificationRepositoryのテスト
/// </summary>
public class NotificationRepositoryTests : UnitTestBase
{
    private RadioDbContext _dbContext = null!;
    private NotificationRepository _repository = null!;

    [SetUp]
    public async Task Setup()
    {
        _dbContext = DbContext;
        _dbContext.ChangeTracker.Clear();
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM Notification");
        _repository = new NotificationRepository(_dbContext);
    }

    /// <summary>
    /// 未読件数と未読一覧が取得できる
    /// </summary>
    [Test]
    public async Task GetUnreadAsync_未読件数と一覧取得()
    {
        await AddNotificationAsync("msg1", isRead: false, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10));
        await AddNotificationAsync("msg2", isRead: true, timestamp: DateTimeOffset.UtcNow.AddMinutes(-5));
        await AddNotificationAsync("msg3", isRead: false, timestamp: DateTimeOffset.UtcNow);

        var count = await _repository.GetUnreadCountAsync();
        var list = await _repository.GetUnreadListAsync();

        Assert.That(count, Is.EqualTo(2));
        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0].Message, Is.EqualTo("msg3"));
    }

    /// <summary>
    /// 指定日時以前を既読にできる
    /// </summary>
    [Test]
    public async Task MarkReadBeforeAsync_既読更新()
    {
        var now = DateTimeOffset.UtcNow;
        await AddNotificationAsync("old", isRead: false, timestamp: now.AddMinutes(-10));
        await AddNotificationAsync("new", isRead: false, timestamp: now.AddMinutes(10));

        await _repository.MarkReadBeforeAsync(now);

        var unread = await _repository.GetUnreadListAsync();
        Assert.That(unread.Count, Is.EqualTo(1));
        Assert.That(unread[0].Message, Is.EqualTo("new"));
    }

    /// <summary>
    /// ページング取得ができる
    /// </summary>
    [Test]
    public async Task GetPagedAsync_ページング取得()
    {
        await AddNotificationAsync("a", isRead: false, timestamp: DateTimeOffset.UtcNow.AddMinutes(-3));
        await AddNotificationAsync("b", isRead: false, timestamp: DateTimeOffset.UtcNow.AddMinutes(-2));
        await AddNotificationAsync("c", isRead: false, timestamp: DateTimeOffset.UtcNow.AddMinutes(-1));

        var (total, list) = await _repository.GetPagedAsync(1, 2);

        Assert.That(total, Is.EqualTo(3));
        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0].Message, Is.EqualTo("c"));
    }

    /// <summary>
    /// 追加と全件削除ができる
    /// </summary>
    [Test]
    public async Task AddAndDeleteAllAsync_追加削除()
    {
        await _repository.AddAsync(new Notification
        {
            Id = Ulid.NewUlid(),
            LogLevel = "Info",
            Category = NoticeCategory.SystemError,
            Message = "added",
            Timestamp = DateTimeOffset.UtcNow.UtcDateTime,
            IsRead = false
        });

        var count = await _repository.GetUnreadCountAsync();
        Assert.That(count, Is.EqualTo(1));

        await _repository.DeleteAllAsync();

        var after = await _repository.GetUnreadCountAsync();
        Assert.That(after, Is.EqualTo(0));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// 通知データを追加
    /// </summary>
    private async Task AddNotificationAsync(string message, bool isRead, DateTimeOffset timestamp)
    {
        _dbContext.Notification.Add(new Notification
        {
            Id = Ulid.NewUlid(),
            LogLevel = "Info",
            Category = NoticeCategory.SystemError,
            Message = message,
            Timestamp = timestamp.UtcDateTime,
            IsRead = isRead
        });
        await _dbContext.SaveChangesAsync();
    }
}
