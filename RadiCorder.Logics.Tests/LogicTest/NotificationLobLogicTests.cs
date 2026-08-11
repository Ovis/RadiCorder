using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Infrastructure.Notification;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest
{
    [TestFixture]
    public class NotificationLobLogicTests : UnitTestBase
    {
        private NotificationLobLogic _notificationLogic;
        private Mock<ILogger<NotificationLobLogic>> _loggerMock;
        private Mock<IRadioAppContext> _appContextMock;
        private Mock<IAppConfigurationService> _configMock;
        private IEntryMapper _entryMapper;
        private RadioDbContext _dbContext;
        private FakeHttpMessageHandler _httpHandler;
        private int _webhookCallCount;
        private INotificationRepository _notificationRepository;

        [SetUp]
        public void Setup()
        {
            _loggerMock = new Mock<ILogger<NotificationLobLogic>>();
            _configMock = new Mock<IAppConfigurationService>();
            _appContextMock = new Mock<IRadioAppContext>();
            _httpHandler = new FakeHttpMessageHandler();
            _webhookCallCount = 0;
            _dbContext = DbContext;
            _notificationRepository = new NotificationRepository(_dbContext);

            _appContextMock.SetupGet(x => x.StandardDateTimeOffset).Returns(DateTimeOffset.Now);
            _entryMapper = new EntryMapper(_configMock.Object);
            var httpClient = new HttpClient(_httpHandler);
            var httpClientFactory = new FakeHttpClientFactory(httpClient);

            _notificationLogic = new NotificationLobLogic(
                _loggerMock.Object,
                _appContextMock.Object,
                _configMock.Object,
                _entryMapper,
                httpClientFactory,
                _notificationRepository);
        }


        [Test]
        public async Task GetUnreadNotificationCountAsync_カウント値テスト()
        {
            var notifications = new List<Notification>
            {
                new() { Id = Ulid.NewUlid(),IsRead = false },
                new() { Id = Ulid.NewUlid(),IsRead = false },
                new() { Id = Ulid.NewUlid(),IsRead = true },
                new() { Id = Ulid.NewUlid(),IsRead = false }
            };

            await _dbContext.Notification.AddRangeAsync(notifications);
            await _dbContext.SaveChangesAsync();

            var count = await _notificationLogic.GetUnreadNotificationCountAsync();

            Assert.That(count, Is.EqualTo(3));
        }


        [Test]
        public async Task GetUnreadNotificationListAsync_並び順テスト()
        {
            // Arrange
            var notifications = new List<Notification>
            {
                new() {Id = Ulid.NewUlid(), IsRead = true, Message = "3日前", Timestamp = DateTime.UtcNow.AddDays(-3) },
                new() {Id = Ulid.NewUlid(), IsRead = false, Message = "1日前", Timestamp = DateTime.UtcNow.AddDays(-1) },
                new() {Id = Ulid.NewUlid(), IsRead = false, Message = "4日前", Timestamp = DateTime.UtcNow.AddDays(-4) },
                new() {Id = Ulid.NewUlid(), IsRead = false, Message = "2日前", Timestamp = DateTime.UtcNow.AddDays(-2) }
            };

            var dbTran = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                await _dbContext.Notification.AddRangeAsync(notifications);
                await _dbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var list = await _notificationLogic.GetUnreadNotificationListAsync();

            Assert.That(list.Count, Is.EqualTo(3));
            Assert.That(list.First().Message, Is.EqualTo("1日前"));
            Assert.That(list.Last().Message, Is.EqualTo("4日前"));
        }


        [Test]
        public async Task UpdateReadNotificationAsync_既読更新処理テスト()
        {
            var dt = DateTime.Now;

            // Arrange
            var notifications = new List<Notification>
            {
                new() { Id = Ulid.NewUlid(), LogLevel = "Information" ,IsRead = false, Timestamp = dt },
                new() { Id = Ulid.NewUlid(), LogLevel = "Information" ,IsRead = false, Timestamp = dt.AddDays(-1) },
                new() { Id = Ulid.NewUlid(), LogLevel = "Information", IsRead = false, Timestamp = dt.AddDays(-2) },
                new() { Id = Ulid.NewUlid(), LogLevel = "Information", IsRead = true, Timestamp = dt.AddDays(-3) },
                new() { Id = Ulid.NewUlid(), LogLevel = "Information", IsRead = false, Timestamp = dt.AddDays(-4) }
            };

            await _dbContext.Notification.AddRangeAsync(notifications);
            await _dbContext.SaveChangesAsync();

            // 実行前確認
            {
                var count = await _notificationLogic.GetUnreadNotificationCountAsync();

                Assert.That(count, Is.EqualTo(4));
            }

            await _notificationLogic.UpdateReadNotificationAsync(dt.AddSeconds(-10));

            // 実行後確認
            {
                var count = await _notificationLogic.GetUnreadNotificationCountAsync();

                Assert.That(count, Is.EqualTo(1));
            }
        }


        [Test]
        public async Task GetNotificationListAsync_データ取得テスト()
        {
            // Arrange
            var notifications = new List<Notification>
            {
                new() {Id = Ulid.NewUlid(), Timestamp = DateTime.UtcNow.AddDays(-1) },
                new() {Id = Ulid.NewUlid(), Timestamp = DateTime.UtcNow.AddDays(-2) },
                new() {Id = Ulid.NewUlid(), Timestamp = DateTime.UtcNow.AddDays(-3) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-4) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-5) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-6) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-7) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-8) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-9) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-10) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-11) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-12) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-13) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-14) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-15) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-16) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-17) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-18) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-19) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-20) },
                new() { Id = Ulid.NewUlid(),Timestamp = DateTime.UtcNow.AddDays(-21) },
            };

            await _dbContext.Notification.AddRangeAsync(notifications);
            await _dbContext.SaveChangesAsync();

            var expectedTotal = notifications.Count;

            // 1ページ目
            {
                var result = await _notificationLogic.GetNotificationListAsync(1, 10);

                // Assert
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Total, Is.EqualTo(expectedTotal));
                Assert.That(result.List?.Count, Is.EqualTo(10));
                Assert.That(result.List?.First().Timestamp, Is.EqualTo(notifications[0].Timestamp));
                Assert.That(result.List?.Last().Timestamp, Is.EqualTo(notifications[9].Timestamp));
            }

            // 2ページ目
            {
                var result = await _notificationLogic.GetNotificationListAsync(2, 10);

                // Assert
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Total, Is.EqualTo(expectedTotal));
                Assert.That(result.List?.Count, Is.EqualTo(10));
                Assert.That(result.List?.First().Timestamp, Is.EqualTo(notifications[10].Timestamp));
                Assert.That(result.List?.Last().Timestamp, Is.EqualTo(notifications[19].Timestamp));
            }

            // 3ページ目
            {
                var result = await _notificationLogic.GetNotificationListAsync(3, 10);

                // Assert
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Total, Is.EqualTo(expectedTotal));
                Assert.That(result.List?.Count, Is.EqualTo(1));
                Assert.That(result.List?.First().Timestamp, Is.EqualTo(notifications[20].Timestamp));
            }

            // 全件取得
            {
                var result = await _notificationLogic.GetNotificationListAsync(1, 100);

                // Assert
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Total, Is.EqualTo(expectedTotal));
                Assert.That(result.List?.Count, Is.EqualTo(21));
                Assert.That(result.List?.First().Timestamp, Is.EqualTo(notifications[0].Timestamp));
                Assert.That(result.List?.Last().Timestamp, Is.EqualTo(notifications[20].Timestamp));
            }
        }


        [Test]
        public async Task DeleteAllNotificationAsync_データ削除テスト()
        {
            var dbTran = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                var notifications = new List<Notification>
                {
                    new() {Id = Ulid.NewUlid(), Timestamp = DateTime.Now },
                    new() {Id = Ulid.NewUlid(), Timestamp = DateTime.Now },
                    new() {Id = Ulid.NewUlid(), Timestamp = DateTime.Now },
                    new() {Id = Ulid.NewUlid(), Timestamp = DateTime.Now },
                    new() {Id = Ulid.NewUlid(), Timestamp = DateTime.Now },
                    new() {Id = Ulid.NewUlid(), Timestamp = DateTime.Now },
                };

                await _dbContext.Notification.AddRangeAsync(notifications);
                await _dbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            await _notificationLogic.DeleteAllNotificationAsync();

            var count = await _dbContext.Notification.CountAsync();

            Assert.That(count, Is.EqualTo(0));
        }


        [Test]
        public async Task SetNotificationAsync_Discord通知_送信される()
        {
            _configMock.SetupGet(x => x.DiscordWebhookUrl).Returns("http://example/webhook");
            _configMock.SetupGet(x => x.NoticeCategories).Returns([NoticeCategory.SystemError]);
            _httpHandler.AddHandler(
                req => req.RequestUri!.ToString().StartsWith("http://example/webhook"),
                _ =>
                {
                    _webhookCallCount++;
                    return new HttpResponseMessage(HttpStatusCode.OK);
                });

            await _notificationLogic.SetNotificationAsync(
                logLevel: LogLevel.Error,
                category: NoticeCategory.SystemError,
                message: "test");

            Assert.That(_webhookCallCount, Is.EqualTo(1));
        }


        [Test]
        public async Task SetNotificationAsync_Discord通知_カテゴリ未指定は送信しない()
        {
            _configMock.SetupGet(x => x.DiscordWebhookUrl).Returns("http://example/webhook");
            _configMock.SetupGet(x => x.NoticeCategories).Returns([]);

            await _notificationLogic.SetNotificationAsync(
                logLevel: LogLevel.Error,
                category: NoticeCategory.SystemError,
                message: "test");

            Assert.That(_webhookCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task GetUnreadNotificationCountAsync_未読バッジ対象カテゴリのみ集計する()
        {
            _configMock.SetupGet(x => x.UnreadBadgeNoticeCategories).Returns([NoticeCategory.SystemError]);

            var notifications = new List<Notification>
            {
                new() { Id = Ulid.NewUlid(), Category = NoticeCategory.SystemError, IsRead = false, Timestamp = DateTime.UtcNow },
                new() { Id = Ulid.NewUlid(), Category = NoticeCategory.RecordingSuccess, IsRead = false, Timestamp = DateTime.UtcNow },
                new() { Id = Ulid.NewUlid(), Category = NoticeCategory.SystemError, IsRead = true, Timestamp = DateTime.UtcNow }
            };

            await _dbContext.Notification.AddRangeAsync(notifications);
            await _dbContext.SaveChangesAsync();

            var count = await _notificationLogic.GetUnreadNotificationCountAsync();

            Assert.That(count, Is.EqualTo(1));
        }


        [TearDown]
        protected async ValueTask DeleteEntry()
        {
            var dbTran = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                _dbContext.Notification.RemoveRange(_dbContext.Notification);

                await _dbContext.SaveChangesAsync();
                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            _httpHandler.Dispose();
        }
    }
}

