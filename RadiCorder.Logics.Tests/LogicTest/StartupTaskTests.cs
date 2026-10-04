using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.Station;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Options;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// StartupTaskのテスト
/// </summary>
public class StartupTaskTests
{
    /// <summary>
    /// FFmpeg未導入時は例外と通知
    /// </summary>
    [Test]
    public async Task InitializeAsync_Ffmpeg未導入_例外と通知()
    {
        var (task, repo) = CreateTarget(ffmpegOk: false);

        Assert.ThrowsAsync<DomainException>(async () => await task.InitializeAsync());

        var list = await repo.GetUnreadListAsync();
        Assert.That(list.Count, Is.EqualTo(2));
        Assert.That(list[0].Message, Does.Contain("ffmpeg"));
        Assert.That(list[1].Message, Does.Contain("ログを確認"));
    }

    /// <summary>
    /// 正常系で通知が増えない
    /// </summary>
    [Test]
    public async Task InitializeAsync_正常完了_通知なし()
    {
        var (task, repo) = CreateTarget(ffmpegOk: true, hasRadikoStations: true, hasRadiruStations: true);

        Assert.DoesNotThrowAsync(async () => await task.InitializeAsync());

        var list = await repo.GetUnreadListAsync();
        Assert.That(list.Count, Is.EqualTo(0));
    }

    /// <summary>
    /// radiko局同期に失敗しても起動継続する
    /// </summary>
    [Test]
    public async Task InitializeAsync_radiko局同期失敗でも起動継続()
    {
        var (task, repo) = CreateTarget(
            ffmpegOk: true,
            hasRadikoStations: true,
            hasRadiruStations: true,
            failRadikoStationSync: true);

        Assert.DoesNotThrowAsync(async () => await task.InitializeAsync());

        var list = await repo.GetUnreadListAsync();
        Assert.That(list.Any(x => x.Message.Contains("radikoの放送局情報同期に失敗")), Is.True);
    }

    [Test]
    public async Task InitializeAsync_radiko局キャッシュ復元はinactive局も含む()
    {
        var configMock = CreateConfig();

        var ffmpegMock = new Mock<IFfmpegService>();
        ffmpegMock.Setup(x => x.Initialize()).Returns(true);

        var radikoHttpHandler = new FakeHttpMessageHandler();
        radikoHttpHandler.AddHandler(
            req => req.RequestUri!.ToString().Contains("member/login"),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"paid_member\":\"0\",\"areafree\":\"0\",\"radiko_session\":\"\"}")
            });
        var radikoHttpClientFactory = new FakeHttpClientFactory(new HttpClient(radikoHttpHandler));

        var radikoLogic = new RadikoUniqueProcessLogic(
            new Mock<ILogger<RadikoUniqueProcessLogic>>().Object,
            configMock.Object,
            radikoHttpClientFactory);

        var appContext = new FakeRadioAppContext();
        var recordJobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configMock.Object);
        var programScheduleLogic = new ProgramScheduleLobLogic(
            new Mock<ILogger<ProgramScheduleLobLogic>>().Object,
            appContext,
            new FakeRadikoApiClient(),
            new FakeRadiruApiClient(),
            new FakeProgramScheduleRepository(),
            recordJobLogic,
            new EntryMapper(configMock.Object));

        var stationRepoMock = new Mock<IStationRepository>();
        stationRepoMock.Setup(x => x.HasAnyRadikoStationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        stationRepoMock.Setup(x => x.GetRadikoStationsAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RadikoStation { StationId = "TBS", StationName = "TBS", IsActive = true },
                new RadikoStation { StationId = "MAJAL", StationName = "MAJ AUDIO LIVE", IsActive = false }
            ]);
        stationRepoMock.Setup(x => x.HasAnyRadiruStationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new HttpClientHandler()));

        var stationLogic = new StationLobLogic(
            new Mock<ILogger<StationLobLogic>>().Object,
            appContext,
            configMock.Object,
            new ThrowingRadikoApiClient(),
            stationRepoMock.Object,
            radikoLogic,
            httpClientFactoryMock.Object,
            new EntryMapper(configMock.Object));

        var notificationRepo = new FakeNotificationRepository();
        var notificationHttpClientFactory = new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler()));
        var notificationLogic = new NotificationLobLogic(
            new Mock<ILogger<NotificationLobLogic>>().Object,
            appContext,
            configMock.Object,
            new EntryMapper(configMock.Object),
            notificationHttpClientFactory,
            notificationRepo);
        var logMaintenanceLobLogic = new LogMaintenanceLobLogic(
            new Mock<ILogger<LogMaintenanceLobLogic>>().Object,
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        var dbPath = Path.Combine(Path.GetTempPath(), $"radiokeep-startup-tests-{Guid.NewGuid():N}.db");
        var dbOptions = new DbContextOptionsBuilder<RadioDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var dbContext = new RadioDbContext(dbOptions);
        dbContext.Database.EnsureCreated();
        var temporaryStorageMaintenanceLobLogic = new TemporaryStorageMaintenanceLobLogic(
            new Mock<ILogger<TemporaryStorageMaintenanceLobLogic>>().Object,
            configMock.Object,
            dbContext);
        var storageCapacityMonitorLobLogic = new StorageCapacityMonitorLobLogic(
            new Mock<ILogger<StorageCapacityMonitorLobLogic>>().Object,
            configMock.Object,
            notificationLogic);

        var task = new StartupTask(
            new Mock<ILogger<StartupTask>>().Object,
            configMock.Object,
            ffmpegMock.Object,
            radikoLogic,
            programScheduleLogic,
            logMaintenanceLobLogic,
            temporaryStorageMaintenanceLobLogic,
            storageCapacityMonitorLobLogic,
            stationLogic,
            notificationLogic);

        await task.InitializeAsync();

        stationRepoMock.Verify(x => x.GetRadikoStationsAsync(false, It.IsAny<CancellationToken>()), Times.Once);
        configMock.Verify(
            x => x.UpdateRadikoStationDic(It.Is<List<RadikoStation>>(list =>
                list.Any(s => s.StationId == "TBS") &&
                list.Any(s => s.StationId == "MAJAL"))),
            Times.Once);
    }

    /// <summary>
    /// StartupTask構築
    /// </summary>
    private static (StartupTask Task, FakeNotificationRepository Repo) CreateTarget(
        bool ffmpegOk,
        bool hasRadikoStations = true,
        bool hasRadiruStations = true,
        bool failRadikoStationSync = false,
        bool failRadikoLogin = false)
    {
        var configMock = CreateConfig();
        if (failRadikoLogin) configMock.Setup(x => x.TryGetRadikoCredentialsAsync()).Throws(new IOException("接続失敗"));

        var ffmpegMock = new Mock<IFfmpegService>();
        ffmpegMock.Setup(x => x.Initialize()).Returns(ffmpegOk);
        var radikoHttpHandler = new FakeHttpMessageHandler();
        radikoHttpHandler.AddHandler(
            req => req.RequestUri!.ToString().Contains("member/login"),
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"paid_member\":\"0\",\"areafree\":\"0\",\"radiko_session\":\"\"}")
            });
        var radikoHttpClientFactory = new FakeHttpClientFactory(new HttpClient(radikoHttpHandler));

        var radikoLogic = new RadikoUniqueProcessLogic(
            new Mock<ILogger<RadikoUniqueProcessLogic>>().Object,
            configMock.Object,
            radikoHttpClientFactory);

        var appContext = new FakeRadioAppContext();
        var recordJobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configMock.Object);

        var programScheduleRepo = new FakeProgramScheduleRepository();
        var programScheduleLogic = new ProgramScheduleLobLogic(
            new Mock<ILogger<ProgramScheduleLobLogic>>().Object,
            appContext,
            new FakeRadikoApiClient(),
            new FakeRadiruApiClient(),
            programScheduleRepo,
            recordJobLogic,
            new EntryMapper(configMock.Object));

        var stationRepoMock = new Mock<IStationRepository>();
        stationRepoMock.Setup(x => x.HasAnyRadikoStationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasRadikoStations);
        stationRepoMock.Setup(x => x.GetRadikoStationsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RadikoStation { StationId = "TBS", StationName = "TBS" }]);
        stationRepoMock.Setup(x => x.HasAnyRadiruStationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasRadiruStations);

        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new HttpClientHandler()));

        IRadikoApiClient radikoApiClient = failRadikoStationSync
            ? new ThrowingRadikoApiClient()
            : new FakeRadikoApiClient
            {
                Stations =
                [
                    new RadikoStation
                    {
                        StationId = "TBS",
                        RegionId = "JP13",
                        StationName = "TBS",
                        IsActive = true
                    }
                ]
            };

        var stationLogic = new StationLobLogic(
            new Mock<ILogger<StationLobLogic>>().Object,
            appContext,
            configMock.Object,
            radikoApiClient,
            stationRepoMock.Object,
            radikoLogic,
            httpClientFactoryMock.Object,
            new EntryMapper(configMock.Object));

        var notificationRepo = new FakeNotificationRepository();
        var notificationHttpClientFactory = new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler()));
        var notificationLogic = new NotificationLobLogic(
            new Mock<ILogger<NotificationLobLogic>>().Object,
            appContext,
            configMock.Object,
            new EntryMapper(configMock.Object),
            notificationHttpClientFactory,
            notificationRepo);
        var logMaintenanceLobLogic = new LogMaintenanceLobLogic(
            new Mock<ILogger<LogMaintenanceLobLogic>>().Object,
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        var dbPath = Path.Combine(Path.GetTempPath(), $"radiokeep-startup-tests-{Guid.NewGuid():N}.db");
        var dbOptions = new DbContextOptionsBuilder<RadioDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var dbContext = new RadioDbContext(dbOptions);
        dbContext.Database.EnsureCreated();
        var temporaryStorageMaintenanceLobLogic = new TemporaryStorageMaintenanceLobLogic(
            new Mock<ILogger<TemporaryStorageMaintenanceLobLogic>>().Object,
            configMock.Object,
            dbContext);
        var storageCapacityMonitorLobLogic = new StorageCapacityMonitorLobLogic(
            new Mock<ILogger<StorageCapacityMonitorLobLogic>>().Object,
            configMock.Object,
            notificationLogic);

        var task = new StartupTask(
            new Mock<ILogger<StartupTask>>().Object,
            configMock.Object,
            ffmpegMock.Object,
            radikoLogic,
            programScheduleLogic,
            logMaintenanceLobLogic,
            temporaryStorageMaintenanceLobLogic,
            storageCapacityMonitorLobLogic,
            stationLogic,
            notificationLogic);

        return (task, notificationRepo);
    }

    [Test]
    public async Task InitializeAsync_radikoログイン障害でも本体の起動を継続する()
    {
        var (task, repo) = CreateTarget(ffmpegOk: true, failRadikoLogin: true);
        await task.InitializeAsync();
        Assert.That((await repo.GetUnreadListAsync()).Any(x => x.Message.Contains("radikoログインに失敗")), Is.True);
    }

    private sealed class ThrowingRadikoApiClient : IRadikoApiClient
    {
        public Task<List<RadikoStation>> GetRadikoStationsAsync(CancellationToken cancellationToken = default)
            => throw new DomainException("radiko station sync failed");

        public Task<List<string>> GetStationsByAreaAsync(string area, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task<List<RadikoProgram>> GetWeeklyProgramsAsync(string stationId, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RadikoProgram>());

        public Task<List<string>> GetRealTimePlaylistUrlsAsync(string stationId, bool isAreaFree, string? requestStationId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());

        public Task<List<string>> GetTimeFreePlaylistCreateUrlsAsync(string stationId, bool isAreaFree, CancellationToken cancellationToken = default)
            => Task.FromResult(new List<string>());
    }

    /// <summary>
    /// 設定モック
    /// </summary>
    private static Mock<IAppConfigurationService> CreateConfig()
    {
        var mock = new Mock<IAppConfigurationService>();

        mock.SetupGet(x => x.RadikoOptions).Returns(new RadikoOptions());
        mock.SetupGet(x => x.RecordStartDuration).Returns(TimeSpan.FromSeconds(5));
        mock.SetupGet(x => x.RecordEndDuration).Returns(TimeSpan.FromSeconds(5));
        mock.SetupGet(x => x.RecordFileSaveDir).Returns(Path.GetTempPath());
        mock.SetupGet(x => x.TemporaryFileSaveDir).Returns(Path.GetTempPath());
        mock.SetupGet(x => x.DiscordWebhookUrl).Returns(string.Empty);
        mock.SetupGet(x => x.StorageLowSpaceThresholdMb).Returns(1024);
        mock.SetupGet(x => x.StorageLowSpaceNotificationCooldownHours).Returns(24);
        mock.SetupGet(x => x.StorageLowSpaceCheckIntervalMinutes).Returns(30);
        mock.SetupGet(x => x.LogRetentionDays).Returns(100);
        mock.SetupGet(x => x.ExternalImportFileTimeZoneId).Returns(TimeZoneInfo.Local.Id);
        mock.SetupGet(x => x.NoticeCategories).Returns([]);
        mock.SetupGet(x => x.RadikoStationDic)
            .Returns(new System.Collections.Concurrent.ConcurrentDictionary<string, string>(
                new[] { new KeyValuePair<string, string>("TBS", "TBS") }));

        mock.Setup(x => x.UpdateRadikoPremiumUser(It.IsAny<bool>()));
        mock.Setup(x => x.UpdateRadikoAreaFree(It.IsAny<bool>()));
        mock.Setup(x => x.UpdateRadikoStationDic(It.IsAny<List<RadikoStation>>()));
        mock.Setup(x => x.TryGetRadikoCredentialsAsync())
            .Returns(ValueTask.FromResult((false, string.Empty, string.Empty)));

        return mock;
    }

}
