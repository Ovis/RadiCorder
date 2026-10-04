using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Infrastructure.Station;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// StationRepositoryのテスト
/// </summary>
public class StationRepositoryTests : UnitTestBase
{
    private RadioDbContext _dbContext = null!;
    private StationRepository _repository = null!;

    [SetUp]
    public async Task Setup()
    {
        _dbContext = DbContext;
        _dbContext.ChangeTracker.Clear();
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM RadikoStations");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreaServices");
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM NhkRadiruAreas");
        _repository = new StationRepository(_dbContext);
    }

    /// <summary>
    /// radiko放送局の存在確認と取得
    /// </summary>
    [Test]
    public async Task RadikoStations_取得確認()
    {
        var hasAnyBefore = await _repository.HasAnyRadikoStationAsync();
        Assert.That(hasAnyBefore, Is.False);

        await _repository.UpsertRadikoStationsAsync([
            new RadikoStation { StationId = "TBS", RegionId = "JP13", IsActive = true }
        ]);

        var hasAnyAfter = await _repository.HasAnyRadikoStationAsync();
        Assert.That(hasAnyAfter, Is.True);

        var list = await _repository.GetRadikoStationsAsync();
        Assert.That(list.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task RadikoStations_既存局を更新し未掲載局を無効化する()
    {
        await _repository.UpsertRadikoStationsAsync([
            new RadikoStation { StationId = "TBS", RegionId = "JP13", StationName = "旧TBS", IsActive = true },
            new RadikoStation { StationId = "OLD", RegionId = "JP13", StationName = "旧局", IsActive = true }
        ]);

        await _repository.UpsertRadikoStationsAsync([
            new RadikoStation { StationId = "TBS", RegionId = "JP13", StationName = "新TBS", IsActive = true },
            new RadikoStation { StationId = "NEW", RegionId = "JP13", StationName = "新局", IsActive = true }
        ]);

        var active = await _repository.GetRadikoStationsAsync();
        var all = await _repository.GetRadikoStationsAsync(activeOnly: false);

        Assert.That(active.Select(x => x.StationId).OrderBy(x => x).ToArray(), Is.EqualTo(new[] { "NEW", "TBS" }));
        Assert.That(active.Single(x => x.StationId == "TBS").StationName, Is.EqualTo("新TBS"));
        Assert.That(all.Single(x => x.StationId == "OLD").IsActive, Is.False);
    }

    [Test]
    public async Task RadikoStations_空入力時は既存局を無効化しない()
    {
        await _repository.UpsertRadikoStationsAsync([
            new RadikoStation { StationId = "TBS", RegionId = "JP13", StationName = "TBS", IsActive = true }
        ]);

        await _repository.UpsertRadikoStationsAsync([]);

        var all = await _repository.GetRadikoStationsAsync(activeOnly: false);
        Assert.That(all.Single(x => x.StationId == "TBS").IsActive, Is.True);
    }

    /// <summary>
    /// らじる★らじる放送局の追加/取得
    /// </summary>
    [Test]
    public async Task RadiruStations_追加更新取得()
    {
        var hasAnyBefore = await _repository.HasAnyRadiruStationAsync();
        Assert.That(hasAnyBefore, Is.False);

        await _repository.UpsertRadiruAreasAndServicesAsync(
        [
            new NhkRadiruArea
            {
                AreaId = "JP13",
                ApiKey = "JP13",
                AreaJpName = "東京",
                ProgramNowOnAirApiUrl = "https://example/noa",
                ProgramDetailApiUrlTemplate = "https://example/detail/{area}",
                DailyProgramApiUrlTemplate = "https://example/day/{area}"
            }
        ],
        [
            new NhkRadiruAreaService
            {
                AreaId = "JP13",
                ServiceId = "r1",
                ServiceName = "R1",
                HlsUrl = "https://example/r1.m3u8",
                IsActive = true
            }
        ]);

        var hasAnyAfter = await _repository.HasAnyRadiruStationAsync();
        Assert.That(hasAnyAfter, Is.True);

        var stations = await _repository.GetRadiruStationsFromAreaServicesAsync();
        Assert.That(stations.Count, Is.EqualTo(1));
        Assert.That(stations[0].AreaId, Is.EqualTo("JP13"));
        Assert.That(stations[0].StationId, Is.EqualTo("r1"));
    }

    [Test]
    public async Task GetRadiruHlsUrlByAreaAndServiceAsync_エリアサービス定義から取得する()
    {
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
            ServiceId = "r1",
            ServiceName = "R1",
            HlsUrl = "https://new.example/r1.m3u8",
            IsActive = true
        });
        await _dbContext.SaveChangesAsync();

        var url = await _repository.GetRadiruHlsUrlByAreaAndServiceAsync("JP13", "r1");

        Assert.That(url, Is.EqualTo("https://new.example/r1.m3u8"));
    }

    [Test]
    public async Task GetRadiruHlsUrlByAreaAndServiceAsync_未登録時はnullを返す()
    {
        var url = await _repository.GetRadiruHlsUrlByAreaAndServiceAsync("JP13", "r1");

        Assert.That(url, Is.Null);
    }

    [Test]
    public async Task UpsertRadiruAreasAndServicesAsync_対象エリアのサービスを置換する()
    {
        await _repository.UpsertRadiruAreasAndServicesAsync(
        [
            new NhkRadiruArea
            {
                AreaId = "130",
                AreaJpName = "東京",
                ApiKey = "130",
                ProgramNowOnAirApiUrl = "https://example/noa",
                ProgramDetailApiUrlTemplate = "https://example/detail/{area}",
                DailyProgramApiUrlTemplate = "https://example/day/{area}"
            }
        ],
        [
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
            }
        ]);

        await _repository.UpsertRadiruAreasAndServicesAsync(
        [
            new NhkRadiruArea
            {
                AreaId = "130",
                AreaJpName = "東京",
                ApiKey = "130",
                ProgramNowOnAirApiUrl = "https://example/noa2",
                ProgramDetailApiUrlTemplate = "https://example/detail2/{area}",
                DailyProgramApiUrlTemplate = "https://example/day2/{area}"
            }
        ],
        [
            new NhkRadiruAreaService
            {
                AreaId = "130",
                ServiceId = "r3",
                ServiceName = "FM",
                HlsUrl = "https://example/r3.m3u8",
                IsActive = true
            }
        ]);

        var area = await _dbContext.NhkRadiruAreas.SingleAsync(x => x.AreaId == "130");
        var services = await _dbContext.NhkRadiruAreaServices
            .Where(x => x.AreaId == "130")
            .OrderBy(x => x.ServiceId)
            .ToListAsync();

        Assert.That(area.ProgramNowOnAirApiUrl, Is.EqualTo("https://example/noa2"));
        Assert.That(services.Select(x => x.ServiceId).ToArray(), Is.EqualTo(new[] { "r3" }));
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.ChangeTracker.Clear();
    }

    [Test]
    public async Task RadiruStations_削除されたエリアの局を無効化して再加入にも対応する()
    {
        NhkRadiruArea Area(string id) => new() { AreaId = id, ApiKey = id, AreaJpName = id };
        NhkRadiruAreaService Service(string id) => new() { AreaId = id, ServiceId = "r1", HlsUrl = "https://example/r1.m3u8", IsActive = true };
        await _repository.UpsertRadiruAreasAndServicesAsync([Area("130"), Area("270")], [Service("130"), Service("270")]);
        await _repository.UpsertRadiruAreasAndServicesAsync([Area("130")], [Service("130")]);
        Assert.That(await _repository.GetActiveRadiruAreaServiceKeysAsync(), Is.EqualTo(new[] { ("130", "r1") }));
        Assert.That(await _dbContext.NhkRadiruAreas.CountAsync(), Is.EqualTo(2));
        Assert.That((await _dbContext.NhkRadiruAreaServices.SingleAsync(x => x.AreaId == "270")).IsActive, Is.False);
        await _repository.UpsertRadiruAreasAndServicesAsync([Area("130"), Area("270")], [Service("130"), Service("270")]);
        Assert.That(await _repository.GetActiveRadiruAreaServiceKeysAsync(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task RadiruStations_不完全な定義では既存局を変更しない()
    {
        var area = new NhkRadiruArea { AreaId = "130", ApiKey = "130", AreaJpName = "東京" };
        await _repository.UpsertRadiruAreasAndServicesAsync([area], [new NhkRadiruAreaService { AreaId = "130", ServiceId = "r1", HlsUrl = "https://example/live.m3u8", IsActive = true }]);
        Assert.ThrowsAsync<RadiCorder.Logics.Errors.DomainException>(async () => await _repository.UpsertRadiruAreasAndServicesAsync([], []));
        Assert.ThrowsAsync<RadiCorder.Logics.Errors.DomainException>(async () => await _repository.UpsertRadiruAreasAndServicesAsync([area], []));
        Assert.That(await _repository.GetActiveRadiruAreaServiceKeysAsync(), Has.Count.EqualTo(1));
    }
}
