using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.Station;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Infrastructure.Station;
using RadiCorder.Logics.Logics.PlayProgramLogic;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.RecordingLogic;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

public class PlayProgramLobLogicTests
{
    private RadioDbContext _dbContext = null!;

    [SetUp]
    public void SetUp()
    {
        var options = new DbContextOptionsBuilder<RadioDbContext>()
            .UseSqlite("Data Source=play-program.db")
            .Options;
        _dbContext = new RadioDbContext(options);
        _dbContext.Database.EnsureDeleted();
        _dbContext.Database.EnsureCreated();
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private static IHttpClientFactory CreateHttpClientFactory(string area, bool isPremium, bool? isAreaFree = null, string? subStations = null)
    {
        var handler = new FakeHttpMessageHandler();

        handler.AddHandler(
            req => req.RequestUri!.ToString().StartsWith("http://radiko.jp/area/"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(area) });

        var effectiveIsAreaFree = isAreaFree ?? isPremium;
        var loginJson = $"{{\"radiko_session\":\"session\",\"paid_member\":\"{(isPremium ? "1" : "0")}\",\"areafree\":\"{(effectiveIsAreaFree ? "1" : "0")}\"}}";
        handler.AddHandler(
            req => req.RequestUri!.ToString().StartsWith("https://radiko.jp/ap/member/webapi/member/login"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(loginJson) });

        handler.AddHandler(
            req => req.RequestUri!.ToString().StartsWith("https://radiko.jp/v2/api/auth1"),
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Add("X-Radiko-AuthToken", "token");
                response.Headers.Add("X-Radiko-KeyLength", "4");
                response.Headers.Add("X-Radiko-KeyOffset", "0");
                return response;
            });

        handler.AddHandler(
            req => req.RequestUri!.ToString().StartsWith("http://radiko.jp/apps/js/playerCommon.js"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("new RadikoJSPlayer('a','b','0123456789ABCDEF'){") });

        handler.AddHandler(
            req => req.RequestUri!.ToString().StartsWith("https://radiko.jp/v2/api/auth2"),
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{area},0,0\n")
                };
                if (!string.IsNullOrWhiteSpace(subStations))
                {
                    response.Headers.Add("x-radiko-substation", subStations);
                }

                return response;
            });

        return new FakeHttpClientFactory(new HttpClient(handler));
    }

    private static ProgramScheduleLobLogic CreateProgramScheduleLobLogic(
        IRadioAppContext appContext,
        IProgramScheduleRepository repository,
        IEntryMapper entryMapper)
    {
        var logger = new Mock<ILogger<ProgramScheduleLobLogic>>().Object;
        var radikoApiClient = new FakeRadikoApiClient();
        var radiruApiClient = new FakeRadiruApiClient();

        var configMock = new Mock<IAppConfigurationService>();
        configMock.SetupGet(c => c.RecordStartDuration).Returns(TimeSpan.Zero);
        configMock.SetupGet(c => c.RecordEndDuration).Returns(TimeSpan.Zero);

        var recordJobLobLogic = new RecordJobLobLogic(
            new Mock<ILogger<RecordJobLobLogic>>().Object,
            configMock.Object);

        return new ProgramScheduleLobLogic(
            logger,
            appContext,
            radikoApiClient,
            radiruApiClient,
            repository,
            recordJobLobLogic,
            entryMapper);
    }

    private (PlayProgramLobLogic Logic, FakeRadikoApiClient ApiClient) CreateTargetCore(
        string stationId,
        bool isPremium,
        bool? isAreaFree,
        List<string> currentAreaStations,
        List<string> realTimeUrls,
        List<string>? realTimeUrlsForAreaFree,
        IProgramScheduleRepository repository,
        string area = "JP13",
        IStationRepository? stationRepository = null,
        string? subStations = null)
    {
        var configMock = new Mock<IAppConfigurationService>();
        configMock.SetupGet(c => c.RadikoOptions).Returns(new Options.RadikoOptions
        {
            RadikoUserId = "user",
            RadikoPassword = "pass"
        });
        configMock.Setup(c => c.TryGetRadikoCredentialsAsync())
            .Returns(ValueTask.FromResult((true, "user", "pass")));
        var dic = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        dic.TryAdd(stationId, "Station");
        configMock.SetupGet(c => c.RadikoStationDic).Returns(dic);
        configMock.Setup(c => c.UpdateRadikoPremiumUser(It.IsAny<bool>()));
        configMock.Setup(c => c.UpdateRadikoAreaFree(It.IsAny<bool>()));

        var entryMapper = new EntryMapper(configMock.Object);
        var appContext = new FakeRadioAppContext();

        var httpClientFactory = CreateHttpClientFactory(area, isPremium, isAreaFree, subStations);
        var radikoLogic = new RadikoUniqueProcessLogic(
            new Mock<ILogger<RadikoUniqueProcessLogic>>().Object,
            configMock.Object,
            httpClientFactory);
        radikoLogic.RefreshRadikoAreaCacheAsync().AsTask().GetAwaiter().GetResult();

        var radikoApiClient = new FakeRadikoApiClient
        {
            StationsByArea = currentAreaStations,
            RealTimeUrls = realTimeUrls,
            RealTimeUrlsForAreaFree = realTimeUrlsForAreaFree ?? realTimeUrls
        };
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new HttpClientHandler()));
        var stationLogic = new StationLobLogic(
            new Mock<ILogger<StationLobLogic>>().Object,
            appContext,
            configMock.Object,
            radikoApiClient,
            stationRepository ?? new FakeStationRepository(),
            radikoLogic,
            httpClientFactoryMock.Object,
            entryMapper);

        var programScheduleLogic = CreateProgramScheduleLobLogic(appContext, repository, entryMapper);

        var logic = new PlayProgramLobLogic(
            new Mock<ILogger<RecordingLobLogic>>().Object,
            _dbContext,
            radikoApiClient,
            radikoLogic,
            programScheduleLogic,
            stationLogic);

        return (logic, radikoApiClient);
    }

    private PlayProgramLobLogic CreateTarget(
        string stationId,
        bool isPremium,
        bool? isAreaFree,
        List<string> currentAreaStations,
        List<string> realTimeUrls,
        List<string>? realTimeUrlsForAreaFree,
        IProgramScheduleRepository repository,
        string area = "JP13",
        IStationRepository? stationRepository = null,
        string? subStations = null)
    {
        return CreateTargetCore(
            stationId,
            isPremium,
            isAreaFree,
            currentAreaStations,
            realTimeUrls,
            realTimeUrlsForAreaFree,
            repository,
            area,
            stationRepository,
            subStations).Logic;
    }

    [Test]
    public async Task PlayRadikoProgramAsync_番組が存在しない場合は失敗()
    {
        var repository = new FakeProgramScheduleRepository
        {
            RadikoProgramById = null
        };

        var logic = CreateTarget("TBS", isPremium: true, isAreaFree: null, currentAreaStations: ["TBS"], realTimeUrls: [], realTimeUrlsForAreaFree: null, repository, area: "JP10");

        var (isSuccess, _, _, error) = await logic.PlayRadikoProgramAsync("P1");

        Assert.That(isSuccess, Is.False);
        Assert.That(error, Is.TypeOf<DomainException>());
        Assert.That(error!.Message, Is.EqualTo("番組情報の取得に失敗しました。"));
    }

    [Test]
    public async Task PlayRadikoProgramAsync_エリア外かつ非プレミアムは失敗()
    {
        var repository = new FakeProgramScheduleRepository
        {
            RadikoProgramById = new RadikoProgram
            {
                ProgramId = "P1",
                StationId = "OUT",
                Title = "Test",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            }
        };

        _dbContext.RadikoStations.Add(new RadikoStation
        {
            StationId = "OUT",
            StationName = "Out Station",
            Area = "JP13",
            RegionId = "R1",
            RegionName = "Region",
            RegionOrder = 1,
            StationOrder = 1,
            AreaFree = false,
            TimeFree = true
        });
        await _dbContext.SaveChangesAsync();

        var logic = CreateTarget("OUT", isPremium: false, isAreaFree: null, currentAreaStations: ["IN"], realTimeUrls: [], realTimeUrlsForAreaFree: null, repository, area: "JP11");

        var (isSuccess, _, _, error) = await logic.PlayRadikoProgramAsync("P1");

        Assert.That(isSuccess, Is.False);
        Assert.That(error, Is.TypeOf<DomainException>());
        Assert.That(error!.Message, Is.EqualTo("この番組は地域が異なるため再生できませんでした。異なる地域の番組を再生する場合はプレミアム会員としてログインする必要があります。"));
    }

    [Test]
    public async Task PlayRadikoProgramAsync_成功時はトークンとURLを返す()
    {
        var repository = new FakeProgramScheduleRepository
        {
            RadikoProgramById = new RadikoProgram
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
        };

        _dbContext.RadikoStations.Add(new RadikoStation
        {
            StationId = "TBS",
            StationName = "TBS",
            Area = "JP13",
            RegionId = "R1",
            RegionName = "Region",
            RegionOrder = 1,
            StationOrder = 1,
            AreaFree = true,
            TimeFree = true
        });
        await _dbContext.SaveChangesAsync();

        var logic = CreateTarget(
            "TBS",
            isPremium: true,
            isAreaFree: null,
            currentAreaStations: ["TBS"],
            realTimeUrls: ["https://si-f-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=TBS&l=15&lsid=test-session&type=b"],
            realTimeUrlsForAreaFree: ["https://si-c-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=TBS&l=15&lsid=af-session&type=c"],
            repository,
            area: "JP12");

        var (isSuccess, token, url, error) = await logic.PlayRadikoProgramAsync("P1");

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(token, Is.EqualTo("token"));
        Assert.That(url, Is.EqualTo("https://si-f-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=TBS&l=15&lsid=test-session&type=b"));
    }

    [Test]
    public async Task PlayRadiruProgramAsync_エリアサービス定義のURLで再生できる()
    {
        _dbContext.NhkRadiruPrograms.Add(new NhkRadiruProgram
        {
            ProgramId = "R1_1",
            AreaId = "JP13",
            StationId = "am",
            Title = "NHK AM",
            Subtitle = "Test",
            RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DaysOfWeek = DaysOfWeek.Monday,
            StartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
            EventId = "ev",
            SiteId = "site"
        });
        _dbContext.NhkRadiruAreas.Add(new NhkRadiruArea
        {
            AreaId = "JP13",
            ApiKey = "JP13",
            AreaJpName = "東京",
            ProgramNowOnAirApiUrl = "https://example/noa",
            ProgramDetailApiUrlTemplate = "https://example/detail/{area}",
            DailyProgramApiUrlTemplate = "https://example/day/{area}"
        });
        _dbContext.NhkRadiruAreaServices.Add(new NhkRadiruAreaService
        {
            AreaId = "JP13",
            ServiceId = "am",
            ServiceName = "NHK AM",
            HlsUrl = "https://new.example/am.m3u8",
            IsActive = true
        });
        await _dbContext.SaveChangesAsync();

        var logic = CreateTarget(
            stationId: "TBS",
            isPremium: true,
            isAreaFree: null,
            currentAreaStations: ["TBS"],
            realTimeUrls: [],
            realTimeUrlsForAreaFree: null,
            repository: new FakeProgramScheduleRepository(),
            stationRepository: new StationRepository(_dbContext));

        var (isSuccess, token, url, error) = await logic.PlayRadiruProgramAsync("R1_1");

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(token, Is.Null);
        Assert.That(url, Is.EqualTo("https://new.example/am.m3u8"));
    }

    [Test]
    public async Task PlayRadiruProgramAsync_エリアサービス未登録時は失敗()
    {
        _dbContext.NhkRadiruPrograms.Add(new NhkRadiruProgram
        {
            ProgramId = "R1_2",
            AreaId = "JP13",
            StationId = "r1",
            Title = "NHK R1",
            Subtitle = "Test",
            RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
            DaysOfWeek = DaysOfWeek.Monday,
            StartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
            EventId = "ev",
            SiteId = "site"
        });
        await _dbContext.SaveChangesAsync();

        var logic = CreateTarget(
            stationId: "TBS",
            isPremium: true,
            isAreaFree: null,
            currentAreaStations: ["TBS"],
            realTimeUrls: [],
            realTimeUrlsForAreaFree: null,
            repository: new FakeProgramScheduleRepository(),
            stationRepository: new StationRepository(_dbContext));

        var (isSuccess, token, url, error) = await logic.PlayRadiruProgramAsync("R1_2");

        Assert.That(isSuccess, Is.False);
        Assert.That(error, Is.TypeOf<DomainException>());
        Assert.That(token, Is.Null);
        Assert.That(url, Is.Null);
    }

    [Test]
    public async Task PlayRadikoProgramAsync_エリア外でもAreaFree有効ならAreaFreeUrlで再生できる()
    {
        var repository = new FakeProgramScheduleRepository
        {
            RadikoProgramById = new RadikoProgram
            {
                ProgramId = "P2",
                StationId = "OUT",
                Title = "Out Of Area",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            }
        };

        _dbContext.RadikoStations.Add(new RadikoStation
        {
            StationId = "OUT",
            StationName = "Out Station",
            Area = "JP27",
            RegionId = "R2",
            RegionName = "Region2",
            RegionOrder = 2,
            StationOrder = 1,
            AreaFree = true,
            TimeFree = true
        });
        await _dbContext.SaveChangesAsync();

        var logic = CreateTarget(
            "OUT",
            isPremium: true,
            isAreaFree: true,
            currentAreaStations: ["IN"],
            realTimeUrls: ["https://si-f-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=OUT&l=15&lsid=normal&type=b"],
            realTimeUrlsForAreaFree: ["https://si-c-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=OUT&l=15&lsid=af&type=c"],
            repository,
            area: "JP14");

        var (isSuccess, token, url, error) = await logic.PlayRadikoProgramAsync("P2");

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(token, Is.EqualTo("token"));
        Assert.That(url, Is.EqualTo("https://si-c-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=OUT&l=15&lsid=af&type=c"));
    }

    [Test]
    public async Task PlayRadikoProgramAsync_エリア外でAreaFree無効ならプレミアム扱いでも失敗()
    {
        var repository = new FakeProgramScheduleRepository
        {
            RadikoProgramById = new RadikoProgram
            {
                ProgramId = "P3",
                StationId = "OUT",
                Title = "Out Of Area",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            }
        };

        _dbContext.RadikoStations.Add(new RadikoStation
        {
            StationId = "OUT",
            StationName = "Out Station",
            Area = "JP27",
            RegionId = "R2",
            RegionName = "Region2",
            RegionOrder = 2,
            StationOrder = 1,
            AreaFree = true,
            TimeFree = true
        });
        await _dbContext.SaveChangesAsync();

        var logic = CreateTarget(
            "OUT",
            isPremium: true,
            isAreaFree: false,
            currentAreaStations: ["IN"],
            realTimeUrls: ["https://si-f-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=OUT&l=15&lsid=normal&type=b"],
            realTimeUrlsForAreaFree: ["https://si-c-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=OUT&l=15&lsid=af&type=c"],
            repository,
            area: "JP15");

        var (isSuccess, _, _, error) = await logic.PlayRadikoProgramAsync("P3");

        Assert.That(isSuccess, Is.False);
        Assert.That(error, Is.TypeOf<DomainException>());
        Assert.That(error!.Message, Is.EqualTo("この番組は地域が異なるため再生できませんでした。異なる地域の番組を再生する場合はプレミアム会員としてログインする必要があります。"));
    }

    [Test]
    public async Task PlayRadikoProgramAsync_SubStation指定があれば解決後のStationIdで再生URLを組み立てる()
    {
        var repository = new FakeProgramScheduleRepository
        {
            RadikoProgramById = new RadikoProgram
            {
                ProgramId = "P4",
                StationId = "ABC",
                Title = "Local Program",
                RadioDate = DateOnly.FromDateTime(DateTime.UtcNow),
                DaysOfWeek = DaysOfWeek.Monday,
                StartTime = DateTimeOffset.UtcNow,
                EndTime = DateTimeOffset.UtcNow.AddMinutes(30),
                AvailabilityTimeFree = AvailabilityTimeFree.Available
            }
        };

        _dbContext.RadikoStations.Add(new RadikoStation
        {
            StationId = "ABC",
            StationName = "ABC",
            Area = "JP27",
            RegionId = "R2",
            RegionName = "Region2",
            RegionOrder = 2,
            StationOrder = 1,
            AreaFree = true,
            TimeFree = true
        });
        await _dbContext.SaveChangesAsync();

        var (logic, apiClient) = CreateTargetCore(
            "ABC",
            isPremium: true,
            isAreaFree: true,
            currentAreaStations: ["ABC"],
            realTimeUrls: ["https://si-f-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=ABC&l=15&lsid=test-session&type=b"],
            realTimeUrlsForAreaFree: ["https://si-c-radiko.smartstream.ne.jp/so/playlist.m3u8?station_id=ABC&l=15&lsid=af-session&type=c"],
            repository: repository,
            area: "JP27",
            subStations: "JP27/ABC/ABC1");

        var (isSuccess, _, _, error) = await logic.PlayRadikoProgramAsync("P4");

        Assert.That(isSuccess, Is.True);
        Assert.That(error, Is.Null);
        Assert.That(apiClient.LastRealTimeRequestStationId, Is.EqualTo("ABC1"));
    }
}
