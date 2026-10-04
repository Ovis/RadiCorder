using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Domain.Station;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Infrastructure.Station;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest
{
    public class StationLobLogicTests : UnitTestBase
    {
        private Mock<ILogger<StationLobLogic>> _loggerMock;
        private Mock<IAppConfigurationService> _configServiceMock;
        private Mock<IRadikoApiClient> _radikoApiClientMock;
        private Mock<IHttpClientFactory> _httpClientFactoryMock;
        private IEntryMapper _entryMapper;
        private RadioDbContext _dbContext;
        private StationLobLogic _stationLogic;
        private Mock<RadikoUniqueProcessLogic> _radikoUniqueProcessLogicMock;
        private IStationRepository _stationRepository;
        private FakeRadioAppContext _appContext;

        [SetUp]
        public async Task Setup()
        {
            _loggerMock = new Mock<ILogger<StationLobLogic>>();
            _configServiceMock = new Mock<IAppConfigurationService>();
            _radikoApiClientMock = new Mock<IRadikoApiClient>();
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(new HttpClientHandler()));
            _dbContext = DbContext;
            _dbContext.ChangeTracker.Clear();
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoStations");
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreaServices");
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreas");
            _entryMapper = new EntryMapper(_configServiceMock.Object);
            _stationRepository = new StationRepository(_dbContext);
            _appContext = new FakeRadioAppContext
            {
                StandardDateTimeOffset = new DateTimeOffset(2026, 4, 12, 12, 0, 0, TimeSpan.FromHours(9))
            };

            _radikoUniqueProcessLogicMock = new Mock<RadikoUniqueProcessLogic>(
                new Mock<ILogger<RadikoUniqueProcessLogic>>().Object,
                _configServiceMock.Object,
                new FakeHttpClientFactory(new HttpClient(new FakeHttpMessageHandler()))
            );

            _stationLogic = new StationLobLogic(
                _loggerMock.Object,
                _appContext,
                _configServiceMock.Object,
                _radikoApiClientMock.Object,
                _stationRepository,
                _radikoUniqueProcessLogicMock.Object,
                _httpClientFactoryMock.Object,
                _entryMapper
            );
        }

        [Test]
        public async Task UpdateRadiruStationInformationAsync_エリアID欠落を削除扱いにしない()
        {
            _dbContext.NhkRadiruAreas.Add(new NhkRadiruArea { AreaId = "130", AreaJpName = "東京" });
            _dbContext.NhkRadiruAreaServices.Add(new NhkRadiruAreaService { AreaId = "130", ServiceId = "r1", IsActive = true, HlsUrl = "https://example.test/live.m3u8" });
            await _dbContext.SaveChangesAsync();
            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(_ => true, _ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<config><stream_url><data><areajp>東京</areajp><r1hls>https://example.test/live.m3u8</r1hls></data><data><areakey>270</areakey><r1hls>https://example.test/osaka.m3u8</r1hls></data></stream_url></config>")
            });
            _httpClientFactoryMock.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
            Assert.ThrowsAsync<DomainException>(async () => await _stationLogic.UpdateRadiruStationInformationAsync());
            Assert.That((await _dbContext.NhkRadiruAreaServices.AsNoTracking().SingleAsync()).IsActive, Is.True);
            Assert.That(await _dbContext.NhkRadiruAreas.CountAsync(), Is.EqualTo(1));
        }

        [Test]
        public void UpsertRadikoStationDefinitionAsync_一部の局ID欠落でも置換を中止する()
        {
            _radikoApiClientMock.Setup(x => x.GetRadikoStationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<RadikoStation> { new() { StationId = "TBS" }, new() { StationId = "" } });
            Assert.ThrowsAsync<DomainException>(async () => await _stationLogic.UpsertRadikoStationDefinitionAsync());
        }

        [Test]
        public async Task CheckInitializedRadikoStationAsync_未初期化状態テスト()
        {
            await using var dbTran = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                await DbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoStations");

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _stationLogic.CheckInitializedRadikoStationAsync();

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public async Task CheckInitializedRadikoStationAsync_初期化状態テスト()
        {
            await using var dbTran = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                await DbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoStations");

                var stationEntry = new RadikoStation
                {
                    StationId = "TBS",
                    RegionId = "JP13"
                };

                _dbContext.RadikoStations.Add(stationEntry);
                await _dbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _stationLogic.CheckInitializedRadikoStationAsync();

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public async Task UpsertRadikoStationDefinitionAsync_放送局リスト取得テスト()
        {
            _radikoApiClientMock
                .Setup(x => x.GetRadikoStationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<RadikoStation>
                {
                    new() { StationId = "TBS", RegionId = "JP13", RegionName = "関東", StationName = "TBS" }
                });

            try
            {
                await _stationLogic.UpsertRadikoStationDefinitionAsync();
            }
            catch (Exception e)
            {
                Assert.Fail(e.Message);
            }

            Assert.Pass();
        }

        [Test]
        public void UpsertRadikoStationDefinitionAsync_空リストは例外()
        {
            _radikoApiClientMock
                .Setup(x => x.GetRadikoStationsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            Assert.ThrowsAsync<DomainException>(async () => await _stationLogic.UpsertRadikoStationDefinitionAsync());
        }

        [Test]
        public async Task GetAllRadikoStationAsync_ShouldReturnListOfRadikoStation()
        {
            await using var dbTran = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                await DbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoStations");

                List<RadikoStation> stations =
                [
                    new()
                    {
                        StationId = "RN1",
                        RegionId = "JP10"
                    },

                    new()
                    {
                        StationId = "JOAK-FM",
                        RegionId = "JP14"
                    }
                ];

                _dbContext.RadikoStations.AddRange(stations);
                await _dbContext.SaveChangesAsync();

                await dbTran.CommitAsync();
            }
            catch (Exception e)
            {
                await dbTran.RollbackAsync();
                Assert.Fail(e.Message);
            }

            var result = await _stationLogic.GetAllRadikoStationAsync();

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.First(r => r.StationId == "RN1").RegionId, Is.EqualTo("JP10"));
        }

        [Test]
        public async Task GetRadiruStationAsync_一覧取得()
        {
            _appContext.StandardDateTimeOffset = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.FromHours(9));

            _dbContext.NhkRadiruAreas.Add(new NhkRadiruArea
            {
                AreaId = "130",
                AreaJpName = "東京",
                ApiKey = "130",
                ProgramNowOnAirApiUrl = "https://example/noa",
                ProgramDetailApiUrlTemplate = "https://example/detail/{area}",
                DailyProgramApiUrlTemplate = "https://example/day/{area}"
            });
            _dbContext.NhkRadiruAreaServices.AddRange(
                new NhkRadiruAreaService
                {
                    AreaId = "130",
                    ServiceId = "r1",
                    ServiceName = "R1",
                    HlsUrl = "https://example/r1.m3u8",
                    IsActive = true
                },
                new NhkRadiruAreaService
                {
                    AreaId = "130",
                    ServiceId = "r2",
                    ServiceName = "R2",
                    HlsUrl = "https://example/r2.m3u8",
                    IsActive = true
                });
            await _dbContext.SaveChangesAsync();

            var list = (await _stationLogic.GetRadiruStationAsync()).ToList();

            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list.Any(x => x.AreaId == "130"), Is.True);
            Assert.That(list.Any(x => x.StationId == "r1"), Is.True);
            Assert.That(list.Any(x => x.StationId == "r2"), Is.True);
            Assert.That(list.Single(x => x.StationId == "r1").StationName, Is.EqualTo("NHK-AM"));
            Assert.That(list.Single(x => x.StationId == "r2").StationName, Is.EqualTo("NHKラジオ第2"));
        }

        [Test]
        public async Task GetRadiruStationAsync_エリアサービス定義から取得()
        {
            _dbContext.NhkRadiruAreas.Add(new NhkRadiruArea
            {
                AreaId = "130",
                AreaJpName = "東京",
                ApiKey = "130",
                ProgramNowOnAirApiUrl = "https://example/noa",
                ProgramDetailApiUrlTemplate = "https://example/detail/{area}",
                DailyProgramApiUrlTemplate = "https://example/day/{area}"
            });
            _dbContext.NhkRadiruAreaServices.Add(new NhkRadiruAreaService
            {
                AreaId = "130",
                ServiceId = "am",
                ServiceName = "NHK AM",
                HlsUrl = "https://example/am.m3u8",
                IsActive = true
            });
            await _dbContext.SaveChangesAsync();

            var list = (await _stationLogic.GetRadiruStationAsync()).ToList();

            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list[0].AreaId, Is.EqualTo("130"));
            Assert.That(list[0].StationId, Is.EqualTo("am"));
            Assert.That(list[0].StationName, Is.EqualTo("不明局(am)"));
        }

        [Test]
        public async Task GetRadiruStationAsync_廃止後1週間経過した局は一覧に出さない()
        {
            _appContext.StandardDateTimeOffset = new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.FromHours(9));

            _dbContext.NhkRadiruAreas.Add(new NhkRadiruArea
            {
                AreaId = "130",
                AreaJpName = "東京",
                ApiKey = "130",
                ProgramNowOnAirApiUrl = "https://example/noa",
                ProgramDetailApiUrlTemplate = "https://example/detail/{area}",
                DailyProgramApiUrlTemplate = "https://example/day/{area}"
            });
            _dbContext.NhkRadiruAreaServices.AddRange(
                new NhkRadiruAreaService
                {
                    AreaId = "130",
                    ServiceId = "r1",
                    ServiceName = "NHK AM",
                    HlsUrl = "https://example/r1.m3u8",
                    IsActive = true
                },
                new NhkRadiruAreaService
                {
                    AreaId = "130",
                    ServiceId = "r2",
                    ServiceName = "NHKラジオ第2",
                    HlsUrl = "https://example/r2.m3u8",
                    IsActive = true
                });
            await _dbContext.SaveChangesAsync();

            var list = (await _stationLogic.GetRadiruStationAsync()).ToList();

            Assert.That(list.Any(x => x.StationId == "r1"), Is.True);
            Assert.That(list.Any(x => x.StationId == "r2"), Is.False);
        }

        [Test]
        public async Task GetActiveRadiruAreaServiceKeysAsync_廃止後も猶予期間中は廃止日前日まで取得対象に含む()
        {
            _appContext.StandardDateTimeOffset = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.FromHours(9));

            _dbContext.NhkRadiruAreas.Add(new NhkRadiruArea
            {
                AreaId = "130",
                AreaJpName = "東京",
                ApiKey = "130",
                ProgramNowOnAirApiUrl = "https://example/noa",
                ProgramDetailApiUrlTemplate = "https://example/detail/{area}",
                DailyProgramApiUrlTemplate = "https://example/day/{area}"
            });
            _dbContext.NhkRadiruAreaServices.AddRange(
                new NhkRadiruAreaService
                {
                    AreaId = "130",
                    ServiceId = "r1",
                    ServiceName = "NHK AM",
                    HlsUrl = "https://example/r1.m3u8",
                    IsActive = true
                },
                new NhkRadiruAreaService
                {
                    AreaId = "130",
                    ServiceId = "r2",
                    ServiceName = "NHKラジオ第2",
                    HlsUrl = "https://example/r2.m3u8",
                    IsActive = true
                });
            await _dbContext.SaveChangesAsync();

            var beforeAbolish = await _stationLogic.GetActiveRadiruAreaServiceKeysAsync(
                new DateTimeOffset(2026, 3, 30, 12, 0, 0, TimeSpan.FromHours(9)));
            var afterAbolish = await _stationLogic.GetActiveRadiruAreaServiceKeysAsync(
                new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.FromHours(9)));

            Assert.That(beforeAbolish.Any(x => x.ServiceId == "r2"), Is.True);
            Assert.That(afterAbolish.Any(x => x.ServiceId == "r2"), Is.False);
        }

        [Test]
        public async Task UpdateRadiruStationInformationAsync_未知サービスIDを保持して保存()
        {
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreaServices");
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreas");

            var xml = """
                      <root>
                        <url_program_noa>https://example/noa/{area}</url_program_noa>
                        <url_program_detail>https://example/detail/{area}</url_program_detail>
                        <url_program_day>https://example/day/{area}</url_program_day>
                        <stream_url>
                          <data>
                            <areajp>東京</areajp>
                            <areakey>130</areakey>
                            <apikey>130</apikey>
                            <r1hls>https://example/r1.m3u8</r1hls>
                            <amhls>https://example/am.m3u8</amhls>
                          </data>
                        </stream_url>
                      </root>
                      """;

            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(
                _ => true,
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(xml) });

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(handler));

            var localStationLogic = new StationLobLogic(
                _loggerMock.Object,
                _appContext,
                _configServiceMock.Object,
                _radikoApiClientMock.Object,
                _stationRepository,
                _radikoUniqueProcessLogicMock.Object,
                httpClientFactoryMock.Object,
                _entryMapper
            );

            var result = await localStationLogic.UpdateRadiruStationInformationAsync();

            var area = await _dbContext.NhkRadiruAreas.SingleAsync(x => x.AreaId == "130");
            var services = await _dbContext.NhkRadiruAreaServices
                .Where(x => x.AreaId == "130")
                .OrderBy(x => x.ServiceId)
                .ToListAsync();

            Assert.That(result, Is.True);
            Assert.That(area.AreaJpName, Is.EqualTo("東京"));
            Assert.That(services.Select(x => x.ServiceId).ToArray(), Is.EqualTo(new[] { "am", "r1" }));
        }

        [Test]
        public async Task TryUpdateRadiruStationInformationIfDueAsync_当日確認済みならスキップ()
        {
            _configServiceMock
                .Setup(x => x.GetRadiruStationDefinitionLastCheckedAtAsync())
                .ReturnsAsync(_appContext.StandardDateTimeOffset.ToUniversalTime());

            var httpClientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
            var localStationLogic = new StationLobLogic(
                _loggerMock.Object,
                _appContext,
                _configServiceMock.Object,
                _radikoApiClientMock.Object,
                _stationRepository,
                _radikoUniqueProcessLogicMock.Object,
                httpClientFactoryMock.Object,
                _entryMapper
            );

            var result = await localStationLogic.TryUpdateRadiruStationInformationIfDueAsync();

            Assert.That(result, Is.False);
            _configServiceMock.Verify(x => x.UpdateRadiruStationDefinitionLastCheckedAtAsync(It.IsAny<DateTimeOffset>()), Times.Never);
        }

        [Test]
        public async Task TryUpdateRadiruStationInformationIfDueAsync_未確認なら更新して時刻保存()
        {
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreaServices");
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreas");

            _configServiceMock
                .Setup(x => x.GetRadiruStationDefinitionLastCheckedAtAsync())
                .ReturnsAsync((DateTimeOffset?)null);

            var xml = """
                      <root>
                        <url_program_noa>https://example/noa/{area}</url_program_noa>
                        <url_program_detail>https://example/detail/{area}</url_program_detail>
                        <url_program_day>https://example/day/{area}</url_program_day>
                        <stream_url>
                          <data>
                            <areajp>東京</areajp>
                            <areakey>130</areakey>
                            <apikey>130</apikey>
                            <r1hls>https://example/r1.m3u8</r1hls>
                          </data>
                        </stream_url>
                      </root>
                      """;

            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(
                _ => true,
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(xml) });

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(handler));

            var localStationLogic = new StationLobLogic(
                _loggerMock.Object,
                _appContext,
                _configServiceMock.Object,
                _radikoApiClientMock.Object,
                _stationRepository,
                _radikoUniqueProcessLogicMock.Object,
                httpClientFactoryMock.Object,
                _entryMapper
            );

            var result = await localStationLogic.TryUpdateRadiruStationInformationIfDueAsync();

            var area = await _dbContext.NhkRadiruAreas.SingleAsync(x => x.AreaId == "130");
            var service = await _dbContext.NhkRadiruAreaServices.SingleAsync(x => x.AreaId == "130" && x.ServiceId == "r1");

            Assert.That(result, Is.True);
            Assert.That(area.AreaJpName, Is.EqualTo("東京"));
            Assert.That(service.HlsUrl, Is.EqualTo("https://example/r1.m3u8"));
            _configServiceMock.Verify(
                x => x.UpdateRadiruStationDefinitionLastCheckedAtAsync(
                    It.Is<DateTimeOffset>(v => v == _appContext.StandardDateTimeOffset.ToUniversalTime())),
                Times.Once);
        }

        [Test]
        public async Task TryUpdateRadiruStationInformationIfDueAsync_取得失敗でも例外を投げず継続できる()
        {
            _configServiceMock
                .Setup(x => x.GetRadiruStationDefinitionLastCheckedAtAsync())
                .ReturnsAsync((DateTimeOffset?)null);

            var handler = new FakeHttpMessageHandler();
            handler.AddHandler(
                _ => true,
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));

            var httpClientFactoryMock = new Mock<IHttpClientFactory>();
            httpClientFactoryMock
                .Setup(x => x.CreateClient(It.IsAny<string>()))
                .Returns(new HttpClient(handler));

            var localStationLogic = new StationLobLogic(
                _loggerMock.Object,
                _appContext,
                _configServiceMock.Object,
                _radikoApiClientMock.Object,
                _stationRepository,
                _radikoUniqueProcessLogicMock.Object,
                httpClientFactoryMock.Object,
                _entryMapper
            );

            var result = await localStationLogic.TryUpdateRadiruStationInformationIfDueAsync();

            Assert.That(result, Is.False);
            _configServiceMock.Verify(x => x.UpdateRadiruStationDefinitionLastCheckedAtAsync(It.IsAny<DateTimeOffset>()), Times.Never);
        }

        [TearDown]
        public async Task TearDown()
        {
            _dbContext.ChangeTracker.Clear();
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoStations");
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreaServices");
            await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreas");
        }
    }
}
