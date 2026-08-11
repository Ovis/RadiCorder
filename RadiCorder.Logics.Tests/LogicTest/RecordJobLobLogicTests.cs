using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

[TestFixture]
public class RecordJobLobLogicTests
{
    private RecordJobLobLogic _recordJobLogic = null!;

    [SetUp]
    public void Setup()
    {
        var configServiceMock = new Mock<IAppConfigurationService>();
        configServiceMock.SetupGet(x => x.RecordStartDuration).Returns(TimeSpan.FromSeconds(15));
        configServiceMock.SetupGet(x => x.RecordEndDuration).Returns(TimeSpan.FromSeconds(30));

        _recordJobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configServiceMock.Object);
    }

    [Test]
    public async Task SetScheduleJobAsync_有効なジョブで成功する()
    {
        var job = new ScheduleJob
        {
            Id = Ulid.NewUlid(),
            ServiceKind = RadioServiceKind.Radiko,
            StationId = "TBS",
            ProgramId = "P1",
            StartDateTime = DateTimeOffset.UtcNow.AddMinutes(5),
            EndDateTime = DateTimeOffset.UtcNow.AddMinutes(35),
            Title = "TestTitle",
            RecordingType = RecordingType.RealTime,
            ReserveType = ReserveType.Program,
            IsEnabled = true
        };

        var result = await _recordJobLogic.SetScheduleJobAsync(job);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task DeleteScheduleJobAsync_存在しないジョブでも成功する()
    {
        var result = await _recordJobLogic.DeleteScheduleJobAsync(Ulid.NewUlid());

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task SetScheduleJobAsync_DB更新後にスケジューラ起床通知を送る()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<RadioDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using var dbContext = new RadioDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var job = new ScheduleJob
            {
                Id = Ulid.NewUlid(),
                ServiceKind = RadioServiceKind.Radiko,
                StationId = "TBS",
                ProgramId = "P1",
                StartDateTime = DateTimeOffset.UtcNow.AddMinutes(5),
                EndDateTime = DateTimeOffset.UtcNow.AddMinutes(35),
                Title = "TestTitle",
                RecordingType = RecordingType.TimeFree,
                ReserveType = ReserveType.Program,
                IsEnabled = true
            };
            dbContext.ScheduleJob.Add(job);
            await dbContext.SaveChangesAsync();

            var wakeupMock = new Mock<IRecordingScheduleWakeup>();
            var providerMock = new Mock<IServiceProvider>();
            providerMock.Setup(x => x.GetService(typeof(RadioDbContext))).Returns(dbContext);

            var scopeMock = new Mock<IServiceScope>();
            scopeMock.SetupGet(x => x.ServiceProvider).Returns(providerMock.Object);

            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            scopeFactoryMock.Setup(x => x.CreateScope()).Returns(scopeMock.Object);

            var configServiceMock = new Mock<IAppConfigurationService>();
            configServiceMock.SetupGet(x => x.RecordStartDuration).Returns(TimeSpan.FromSeconds(15));
            configServiceMock.SetupGet(x => x.RecordEndDuration).Returns(TimeSpan.FromSeconds(30));

            var logic = new RecordJobLobLogic(
                new Mock<ILogger<RecordJobLobLogic>>().Object,
                configServiceMock.Object,
                scopeFactoryMock.Object,
                wakeupMock.Object);

            var result = await logic.SetScheduleJobAsync(job);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Error, Is.Null);
            wakeupMock.Verify(x => x.Wake(), Times.Once);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try
                {
                    File.Delete(dbPath);
                }
                catch
                {
                    // テスト本体の成否に影響させない
                }
            }
        }
    }

}
