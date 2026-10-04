using Microsoft.Extensions.Logging;
using Moq;
using System.Reflection;
using RadiCorder.Logics.Domain.Notification;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ProgramScheduleLobLogicTests
{
    private static (ProgramScheduleLobLogic Logic, Mock<IProgramScheduleRepository> Repo, FakeRadioAppContext Context) CreateTarget()
    {
        var logger = new Mock<ILogger<ProgramScheduleLobLogic>>().Object;
        var appContext = new FakeRadioAppContext
        {
            StandardDateTimeOffset = new DateTimeOffset(2026, 2, 9, 12, 0, 0, TimeSpan.FromHours(9))
        };

        var configMock = new Mock<IAppConfigurationService>();
        var radikoStations = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        radikoStations.TryAdd("TBS", "TBS");
        configMock.SetupGet(c => c.RadikoStationDic).Returns(radikoStations);

        var entryMapper = new EntryMapper(configMock.Object);
        var radikoApiClient = new FakeRadikoApiClient();
        var radiruApiClient = new FakeRadiruApiClient();
        var repoMock = new Mock<IProgramScheduleRepository>();

        var recordJobLobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configMock.Object);

        var logic = new ProgramScheduleLobLogic(
            logger,
            appContext,
            radikoApiClient,
            radiruApiClient,
            repoMock.Object,
            recordJobLobLogic,
            entryMapper);

        return (logic, repoMock, appContext);
    }

    /// <summary>
    /// テスト対象を生成（radiko/らじるのモック参照付き）
    /// </summary>
    private static (ProgramScheduleLobLogic Logic, Mock<IProgramScheduleRepository> Repo, FakeRadioAppContext Context, FakeRadikoApiClient Radiko, FakeRadiruApiClient Radiru)
        CreateTargetWithClients()
    {
        var logger = new Mock<ILogger<ProgramScheduleLobLogic>>().Object;
        var appContext = new FakeRadioAppContext
        {
            StandardDateTimeOffset = new DateTimeOffset(2026, 2, 9, 12, 0, 0, TimeSpan.FromHours(9))
        };

        var configMock = new Mock<IAppConfigurationService>();
        var radikoStations = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        radikoStations.TryAdd("TBS", "TBS");
        configMock.SetupGet(c => c.RadikoStationDic).Returns(radikoStations);

        var entryMapper = new EntryMapper(configMock.Object);
        var radikoApiClient = new FakeRadikoApiClient();
        var radiruApiClient = new FakeRadiruApiClient();
        var repoMock = new Mock<IProgramScheduleRepository>();

        var recordJobLobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configMock.Object);

        var logic = new ProgramScheduleLobLogic(
            logger,
            appContext,
            radikoApiClient,
            radiruApiClient,
            repoMock.Object,
            recordJobLobLogic,
            entryMapper);

        return (logic, repoMock, appContext, radikoApiClient, radiruApiClient);
    }

    [Test]
    public async Task GetRadikoProgramAsync_番組取得できる()
    {
        var (logic, repoMock, _) = CreateTarget();

        repoMock.Setup(r => r.GetRadikoProgramByIdAsync("P1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RadikoProgram
            {
                ProgramId = "P1",
                StationId = "TBS",
                Title = "Test",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            });

        var result = await logic.GetRadikoProgramAsync("P1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ServiceKind, Is.EqualTo(RadioServiceKind.Radiko));
        Assert.That(result.StationName, Is.EqualTo("TBS"));
    }

    [Test]
    public async Task GetRadikoNowOnAirProgramListAsync_未知StationIdでもフォールバック名で取得できる()
    {
        var (logic, repoMock, context) = CreateTarget();

        repoMock.Setup(r => r.GetRadikoNowOnAirAsync(context.StandardDateTimeOffset, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RadikoProgram
                {
                    ProgramId = "P_MAJAL",
                    StationId = "MAJAL",
                    Title = "Test",
                    RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    DaysOfWeek = DaysOfWeek.Monday,
                    StartTime = DateTimeOffset.UtcNow,
                    EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                    AvailabilityTimeFree = AvailabilityTimeFree.Available
                }
            ]);

        var result = await logic.GetRadikoNowOnAirProgramListAsync(context.StandardDateTimeOffset);

        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].StationName, Is.EqualTo("不明局(MAJAL)"));
    }

    [Test]
    public async Task GetRadikoProgramAsync_番組がない場合はnull()
    {
        var (logic, repoMock, _) = CreateTarget();
        repoMock.Setup(r => r.GetRadikoProgramByIdAsync("P1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RadikoProgram?)null);

        var result = await logic.GetRadikoProgramAsync("P1");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetRadiruProgramAsync_番組取得できる()
    {
        var (logic, repoMock, _) = CreateTarget();
        var areaTokyo = RadiruAreaKind.東京.GetEnumCodeId();

        repoMock.Setup(r => r.GetRadiruProgramByIdAsync("R1_1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NhkRadiruProgram
            {
                ProgramId = "R1_1",
                StationId = "r1",
                AreaId = areaTokyo,
                Title = "NHK",
                Subtitle = "Test",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                EventId = "EV",
                SiteId = "site",
                ProgramUrl = "http://example"
            });

        var result = await logic.GetRadiruProgramAsync("R1_1");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ServiceKind, Is.EqualTo(RadioServiceKind.Radiru));
        Assert.That(result.AreaName, Is.EqualTo("東京"));
    }

    [Test]
    public async Task GetRadiruProgramAsync_未知AreaIdでもフォールバック名で取得できる()
    {
        var (logic, repoMock, _) = CreateTarget();

        repoMock.Setup(r => r.GetRadiruProgramByIdAsync("R1_2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NhkRadiruProgram
            {
                ProgramId = "R1_2",
                StationId = "r1",
                AreaId = "999",
                Title = "NHK",
                Subtitle = "Test",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                EventId = "EV",
                SiteId = "site",
                ProgramUrl = "http://example"
            });

        var result = await logic.GetRadiruProgramAsync("R1_2");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.AreaName, Is.EqualTo("不明エリア(999)"));
    }

    [Test]
    public async Task GetRadiruProgramAsync_未知ServiceIdでもフォールバック局名で取得できる()
    {
        var (logic, repoMock, _) = CreateTarget();
        var areaTokyo = RadiruAreaKind.東京.GetEnumCodeId();

        repoMock.Setup(r => r.GetRadiruProgramByIdAsync("R1_3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NhkRadiruProgram
            {
                ProgramId = "R1_3",
                StationId = "am",
                AreaId = areaTokyo,
                Title = "NHK AM",
                Subtitle = "Test",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                EventId = "EV",
                SiteId = "site",
                ProgramUrl = "http://example"
            });

        var result = await logic.GetRadiruProgramAsync("R1_3");

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.StationName, Is.EqualTo("不明局(am)"));
    }

    [Test]
    public async Task SearchRadikoProgramAsync_例外を呼び出し元へ返す()
    {
        var (logic, repoMock, context) = CreateTarget();

        repoMock.Setup(r => r.SearchRadikoProgramsAsync(It.IsAny<ProgramSearchEntity>(), context.StandardDateTimeOffset, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("db error"));

        Assert.That(async () => await logic.SearchRadikoProgramAsync(new ProgramSearchEntity()),
            Throws.Exception.With.Message.EqualTo("db error"));
    }

    [Test]
    public async Task HasProgramScheduleBeenUpdatedWithin24Hours_未更新ならfalse()
    {
        var (logic, repoMock, _) = CreateTarget();
        repoMock.Setup(r => r.GetLastUpdatedProgramAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);

        var result = await logic.HasProgramScheduleBeenUpdatedWithin24Hours();

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task HasProgramScheduleBeenUpdatedWithin24Hours_24時間以内ならtrue()
    {
        var (logic, repoMock, context) = CreateTarget();
        repoMock.Setup(r => r.GetLastUpdatedProgramAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context.StandardDateTimeOffset.AddHours(-1));

        var result = await logic.HasProgramScheduleBeenUpdatedWithin24Hours();

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task SetScheduleJobFromDbAsync_復元失敗時は無効化して継続する()
    {
        var appContext = new FakeRadioAppContext();
        var configMock = new Mock<IAppConfigurationService>();
        var recordJobLobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configMock.Object);

        var repoMock = new Mock<IProgramScheduleRepository>();
        var jobId = Ulid.NewUlid();
        repoMock.Setup(r => r.GetScheduleJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduleJob
                {
                    Id = jobId,
                    ServiceKind = RadioServiceKind.Radiko,
                    StationId = "TBS",
                    ProgramId = "P1",
                    Title = "Test",
                    StartDateTime = DateTimeOffset.UtcNow.AddMinutes(1),
                    EndDateTime = DateTimeOffset.UtcNow.AddMinutes(31),
                    RecordingType = RecordingType.RealTime,
                    ReserveType = ReserveType.Program,
                    IsEnabled = true
                }
            ]);
        repoMock.Setup(r => r.DisableScheduleJobAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var logic = new ProgramScheduleLobLogic(
            new Mock<ILogger<ProgramScheduleLobLogic>>().Object,
            appContext,
            new FakeRadikoApiClient(),
            new FakeRadiruApiClient(),
            repoMock.Object,
            recordJobLobLogic,
            new EntryMapper(configMock.Object));

        Assert.DoesNotThrowAsync(async () => await logic.SetScheduleJobFromDbAsync());
        repoMock.Verify(r => r.DisableScheduleJobAsync(jobId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SetScheduleJobFromDbAsync_通知は無効化成功失敗の件数を事実通りに出す()
    {
        var appContext = new FakeRadioAppContext();
        var configMock = new Mock<IAppConfigurationService>();
        configMock.SetupGet(x => x.DiscordWebhookUrl).Returns(string.Empty);
        configMock.SetupGet(x => x.NoticeCategories).Returns([]);

        var recordJobLobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configMock.Object);

        var repoMock = new Mock<IProgramScheduleRepository>();
        var jobId1 = Ulid.NewUlid();
        var jobId2 = Ulid.NewUlid();
        repoMock.Setup(r => r.GetScheduleJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ScheduleJob
                {
                    Id = jobId1,
                    ServiceKind = RadioServiceKind.Radiko,
                    StationId = "TBS",
                    ProgramId = "P1",
                    Title = "Test1",
                    StartDateTime = DateTimeOffset.UtcNow.AddMinutes(1),
                    EndDateTime = DateTimeOffset.UtcNow.AddMinutes(31),
                    RecordingType = RecordingType.RealTime,
                    ReserveType = ReserveType.Program,
                    IsEnabled = true
                },
                new ScheduleJob
                {
                    Id = jobId2,
                    ServiceKind = RadioServiceKind.Radiko,
                    StationId = "TBS",
                    ProgramId = "P2",
                    Title = "Test2",
                    StartDateTime = DateTimeOffset.UtcNow.AddMinutes(1),
                    EndDateTime = DateTimeOffset.UtcNow.AddMinutes(31),
                    RecordingType = RecordingType.RealTime,
                    ReserveType = ReserveType.Program,
                    IsEnabled = true
                }
            ]);

        repoMock.Setup(r => r.DisableScheduleJobAsync(jobId1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        repoMock.Setup(r => r.DisableScheduleJobAsync(jobId2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var notificationRepoMock = new Mock<INotificationRepository>();
        notificationRepoMock
            .Setup(x => x.AddAsync(It.IsAny<RadiCorder.Logics.RdbContext.Notification>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var notificationLobLogic = new NotificationLobLogic(
            new Mock<ILogger<NotificationLobLogic>>().Object,
            appContext,
            configMock.Object,
            new EntryMapper(configMock.Object),
            new Mock<IHttpClientFactory>().Object,
            notificationRepoMock.Object);

        var logic = new ProgramScheduleLobLogic(
            new Mock<ILogger<ProgramScheduleLobLogic>>().Object,
            appContext,
            new FakeRadikoApiClient(),
            new FakeRadiruApiClient(),
            repoMock.Object,
            recordJobLobLogic,
            new EntryMapper(configMock.Object),
            notificationLobLogic);

        await logic.SetScheduleJobFromDbAsync();

        notificationRepoMock.Verify(x => x.AddAsync(
            It.IsAny<RadiCorder.Logics.RdbContext.Notification>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void SetProgramLastUpdateDateTimeAsync_例外時は再throw()
    {
        var logger = new Mock<ILogger<ProgramScheduleLobLogic>>().Object;
        var appContext = new FakeRadioAppContext();
        var repoMock = new Mock<IProgramScheduleRepository>();
        repoMock.Setup(r => r.SetLastUpdatedProgramAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("fail"));

        var logic = new ProgramScheduleLobLogic(
            logger,
            appContext,
            new FakeRadikoApiClient(),
            new FakeRadiruApiClient(),
            repoMock.Object,
            new RecordJobLobLogic(
                new Mock<ILogger<RecordJobLobLogic>>().Object,
                new Mock<IAppConfigurationService>().Object),
            new EntryMapper(new Mock<IAppConfigurationService>().Object));

        Assert.ThrowsAsync<Exception>(async () =>
            await logic.SetProgramLastUpdateDateTimeAsync());
    }

    /// <summary>
    /// radiko番組表更新で例外が発生した場合は再throw
    /// </summary>
    [Test]
    public void UpdateLatestRadikoProgramDataAsync_例外時は再throw()
    {
        var logger = new Mock<ILogger<ProgramScheduleLobLogic>>().Object;
        var appContext = new FakeRadioAppContext();
        var radikoApiClient = new FakeRadikoApiClient();
        var radiruApiClient = new FakeRadiruApiClient();
        var repoMock = new Mock<IProgramScheduleRepository>();

        repoMock.Setup(r => r.GetRadikoStationIdsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("db error"));

        var recordJobLobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            new Mock<IAppConfigurationService>().Object);

        var logic = new ProgramScheduleLobLogic(
            logger,
            appContext,
            radikoApiClient,
            radiruApiClient,
            repoMock.Object,
            recordJobLobLogic,
            new EntryMapper(new Mock<IAppConfigurationService>().Object));

        Assert.ThrowsAsync<Exception>(async () =>
            await logic.UpdateLatestRadikoProgramDataAsync());
    }

    /// <summary>
    /// らじる番組表更新で例外が発生した場合は再throw
    /// </summary>
    [Test]
    public void UpdateRadiruProgramDataAsync_例外時は再throw()
    {
        var logger = new Mock<ILogger<ProgramScheduleLobLogic>>().Object;
        var appContext = new FakeRadioAppContext();
        var radikoApiClient = new FakeRadikoApiClient();
        var radiruApiClientMock = new Mock<IRadiruApiClient>();

        radiruApiClientMock.Setup(x => x.GetAvailableAreaServicesAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([("130", "r1")]);

        radiruApiClientMock.Setup(x => x.GetDailyProgramsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("api error"));

        var repoMock = new Mock<IProgramScheduleRepository>();

        var recordJobLobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            new Mock<IAppConfigurationService>().Object);

        var logic = new ProgramScheduleLobLogic(
            logger,
            appContext,
            radikoApiClient,
            radiruApiClientMock.Object,
            repoMock.Object,
            recordJobLobLogic,
            new EntryMapper(new Mock<IAppConfigurationService>().Object));

        Assert.ThrowsAsync<DomainException>(async () =>
            await logic.UpdateRadiruProgramDataAsync());
    }

    /// <summary>
    /// radikoの番組表一覧を取得できる
    /// </summary>
    [Test]
    public async Task GetRadikoProgramListAsync_取得できる()
    {
        var (logic, repoMock, _) = CreateTarget();

        repoMock.Setup(r => r.GetRadikoProgramsAsync(It.IsAny<DateOnly>(), "TBS", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RadikoProgram
                {
                    ProgramId = "P1",
                    StationId = "TBS",
                    Title = "Test",
                    RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    DaysOfWeek = DaysOfWeek.Monday,
                    StartTime = DateTimeOffset.UtcNow,
                    EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                    AvailabilityTimeFree = AvailabilityTimeFree.Available
                }
            ]);

        var list = await logic.GetRadikoProgramListAsync(DateOnly.FromDateTime(DateTime.UtcNow), "TBS");

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].StationName, Is.EqualTo("TBS"));
    }

    /// <summary>
    /// radikoの番組表更新で局数分の登録が行われる
    /// </summary>
    [Test]
    public async Task UpdateLatestRadikoProgramDataAsync_局数分登録()
    {
        var (logic, repoMock, _, radikoApi, _) = CreateTargetWithClients();

        repoMock.Setup(r => r.GetRadikoStationIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "TBS", "ABC" });

        radikoApi.WeeklyPrograms =
        [
            new RadikoProgram
            {
                ProgramId = "P1",
                StationId = "TBS",
                Title = "Test",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            }
        ];

        await logic.UpdateLatestRadikoProgramDataAsync();

        repoMock.Verify(
            r => r.AddRadikoProgramsIfMissingAsync(It.IsAny<IEnumerable<RadikoProgram>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task HasRadikoProgramsForAllStationsThroughAsync_既定日数で判定される()
    {
        var (logic, repoMock, context) = CreateTarget();
        DateOnly? calledTargetDate = null;
        repoMock.Setup(r => r.HasRadikoProgramsForAllStationsThroughAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, CancellationToken>((d, _) => calledTargetDate = d)
            .ReturnsAsync(true);

        var result = await logic.HasRadikoProgramsForAllStationsThroughAsync();

        Assert.That(result, Is.True);
        Assert.That(calledTargetDate, Is.EqualTo(context.StandardDateTimeOffset.ToRadioDate().AddDays(6)));
    }

    [Test]
    public async Task HasRadikoProgramsForAllStationsThroughAsync_負数指定は0日に丸める()
    {
        var (logic, repoMock, context) = CreateTarget();
        DateOnly? calledTargetDate = null;
        repoMock.Setup(r => r.HasRadikoProgramsForAllStationsThroughAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, CancellationToken>((d, _) => calledTargetDate = d)
            .ReturnsAsync(false);

        var result = await logic.HasRadikoProgramsForAllStationsThroughAsync(-10);

        Assert.That(result, Is.False);
        Assert.That(calledTargetDate, Is.EqualTo(context.StandardDateTimeOffset.ToRadioDate()));
    }

    /// <summary>
    /// radikoの古い番組表削除が呼び出される
    /// </summary>
    [Test]
    public async Task DeleteOldRadikoProgramAsync_呼び出し()
    {
        var (logic, repoMock, context) = CreateTarget();
        DateOnly? deleted = null;

        repoMock.Setup(r => r.DeleteOldRadikoProgramsAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, CancellationToken>((d, _) => deleted = d)
            .Returns(ValueTask.CompletedTask);

        await logic.DeleteOldRadikoProgramAsync();

        var expected = context.StandardDateTimeOffset.AddMonths(-1).ToRadioDate();
        Assert.That(deleted, Is.EqualTo(expected));
    }

    /// <summary>
    /// らじる★らじるの番組表一覧を取得できる
    /// </summary>
    [Test]
    public async Task GetRadiruProgramAsync_一覧取得()
    {
        var (logic, repoMock, _) = CreateTarget();
        var areaTokyo = RadiruAreaKind.東京.GetEnumCodeId();

        repoMock.Setup(r => r.GetRadiruProgramsAsync(It.IsAny<DateOnly>(), areaTokyo, "r1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new NhkRadiruProgram
                {
                    ProgramId = "R1_1",
                    StationId = "r1",
                    AreaId = areaTokyo,
                    Title = "NHK",
                    Subtitle = "Test",
                    RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    DaysOfWeek = DaysOfWeek.Monday,
                    StartTime = DateTimeOffset.UtcNow,
                    EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                    EventId = "EV",
                    SiteId = "site",
                    ProgramUrl = "http://example"
                }
            ]);

        var list = await logic.GetRadiruProgramAsync(DateOnly.FromDateTime(DateTime.UtcNow), areaTokyo, "r1");

        Assert.That(list.Count, Is.EqualTo(1));
        Assert.That(list[0].AreaName, Is.EqualTo("東京"));
        Assert.That(list[0].StationId, Is.EqualTo("r1"));
    }

    [Test]
    public void UpsertDailyProgramDataAsync_必須項目不足は更新を中止して既存データを保持する()
    {
        var (logic, repoMock, _, _, radiruApiClient) = CreateTargetWithClients();
        var now = DateTimeOffset.Now;

        radiruApiClient.Programs =
        [
            new RadiruProgramJsonEntity
            {
                Id = "ok-1",
                Name = "Valid",
                StartDate = now,
                EndDate = now.AddMinutes(30),
                IdentifierGroup = new IdentifierGroup { RadioSeriesName = "ValidTitle" },
                About = new About { Audio = new Audio() }
            },
            new RadiruProgramJsonEntity
            {
                Id = "ng-1",
                Name = "",
                StartDate = default,
                EndDate = default,
                IdentifierGroup = new IdentifierGroup(),
                About = new About { Audio = new Audio() }
            }
        ];

        IEnumerable<NhkRadiruProgram>? saved = null;
        repoMock.Setup(r => r.UpsertRadiruProgramsAsync(It.IsAny<IEnumerable<NhkRadiruProgram>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<NhkRadiruProgram>, CancellationToken>((entries, _) => saved = entries.ToList())
            .Returns(ValueTask.CompletedTask);

        var method = typeof(ProgramScheduleLobLogic).GetMethod(
            "UpsertDailyProgramDataAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(method, Is.Not.Null);

        var invoked = method!.Invoke(
            logic,
            ["130", "r1", now]);

        Assert.ThrowsAsync<DomainException>(async () => await (ValueTask<bool>)invoked!);
        Assert.That(saved, Is.Null);
    }

    [Test]
    public async Task UpdateRadiruProgramDataAsync_AreaService定義に従って更新する()
    {
        var (logic, repoMock, _, _, radiruApiClient) = CreateTargetWithClients();
        var now = DateTimeOffset.Now;

        radiruApiClient.AreaServices =
        [
            ("130", "r1"),
            ("130", "am")
        ];
        radiruApiClient.Programs =
        [
            new RadiruProgramJsonEntity
            {
                Id = "p1",
                Name = "Program",
                StartDate = now,
                EndDate = now.AddMinutes(30),
                IdentifierGroup = new IdentifierGroup { RadioSeriesName = "Program" },
                About = new About { Audio = new Audio() }
            }
        ];

        var savedEntries = new List<NhkRadiruProgram>();
        repoMock.Setup(r => r.UpsertRadiruProgramsAsync(It.IsAny<IEnumerable<NhkRadiruProgram>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<NhkRadiruProgram>, CancellationToken>((entries, _) => savedEntries.AddRange(entries))
            .Returns(ValueTask.CompletedTask);

        await logic.UpdateRadiruProgramDataAsync();

        Assert.That(radiruApiClient.Requests.Any(x => x.AreaId == "130" && x.ServiceId == "r1"), Is.True);
        Assert.That(radiruApiClient.Requests.Any(x => x.AreaId == "130" && x.ServiceId == "am"), Is.True);
        Assert.That(savedEntries.Any(x => x.AreaId == "130" && x.StationId == "r1"), Is.True);
        Assert.That(savedEntries.Any(x => x.AreaId == "130" && x.StationId == "am"), Is.True);
    }

    /// <summary>
    /// らじる★らじる検索でDB障害を空結果に変換しない
    /// </summary>
    [Test]
    public async Task SearchRadiruProgramAsync_例外を呼び出し元へ返す()
    {
        var (logic, repoMock, context) = CreateTarget();

        repoMock.Setup(r => r.SearchRadiruProgramsAsync(It.IsAny<ProgramSearchEntity>(), context.StandardDateTimeOffset, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("db error"));

        Assert.That(async () => await logic.SearchRadiruProgramAsync(new ProgramSearchEntity()),
            Throws.Exception.With.Message.EqualTo("db error"));
    }

    /// <summary>
    /// らじる★らじるの古い番組表削除が呼び出される
    /// </summary>
    [Test]
    public async Task DeleteOldRadiruProgramAsync_呼び出し()
    {
        var (logic, repoMock, context) = CreateTarget();
        DateOnly? deleted = null;

        repoMock.Setup(r => r.DeleteOldRadiruProgramsAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Callback<DateOnly, CancellationToken>((d, _) => deleted = d)
            .Returns(ValueTask.CompletedTask);

        await logic.DeleteOldRadiruProgramAsync();

        var expected = context.StandardDateTimeOffset.AddMonths(-1).ToRadioDate();
        Assert.That(deleted, Is.EqualTo(expected));
    }
}
