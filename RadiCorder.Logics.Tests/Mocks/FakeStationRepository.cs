using RadiCorder.Logics.Domain.Station;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// 放送局リポジトリのテスト用スタブ
/// </summary>
public class FakeStationRepository : IStationRepository
{
    public List<RadikoStation> RadikoStations { get; set; } = [];
    public NhkRadiruArea? RadiruArea { get; set; }
    public List<RadiruStationEntry> RadiruStationsFromAreaServices { get; set; } = [];
    public List<(string AreaId, string ServiceId)> ActiveRadiruAreaServiceKeys { get; set; } = [];
    public Dictionary<string, string> RadiruStreamUrls { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask<bool> HasAnyRadikoStationAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(RadikoStations.Any(x => x.IsActive));

    public ValueTask<List<RadikoStation>> GetRadikoStationsAsync(bool activeOnly = true, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(activeOnly
            ? RadikoStations.Where(x => x.IsActive).ToList()
            : RadikoStations.ToList());

    public ValueTask UpsertRadikoStationsAsync(IEnumerable<RadikoStation> stations, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var input = stations
            .Where(x => !string.IsNullOrWhiteSpace(x.StationId))
            .GroupBy(x => x.StationId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var ids = input.Select(x => x.StationId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var station in input)
        {
            var existing = RadikoStations.FirstOrDefault(x => string.Equals(x.StationId, station.StationId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                station.IsActive = true;
                station.LastSeenAtUtc = now;
                RadikoStations.Add(station);
                continue;
            }

            existing.RegionId = station.RegionId;
            existing.RegionName = station.RegionName;
            existing.RegionOrder = station.RegionOrder;
            existing.Area = station.Area;
            existing.StationName = station.StationName;
            existing.StationUrl = station.StationUrl;
            existing.LogoPath = station.LogoPath;
            existing.AreaFree = station.AreaFree;
            existing.TimeFree = station.TimeFree;
            existing.StationOrder = station.StationOrder;
            existing.IsActive = true;
            existing.LastSeenAtUtc = now;
        }

        foreach (var station in RadikoStations.Where(x => !ids.Contains(x.StationId)))
        {
            station.IsActive = false;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> HasAnyRadiruStationAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);

    public ValueTask<string?> GetRadiruHlsUrlByAreaAndServiceAsync(
        string areaId,
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var key = $"{areaId}:{serviceId}";
        if (RadiruStreamUrls.TryGetValue(key, out var url))
        {
            return ValueTask.FromResult<string?>(url);
        }
        return ValueTask.FromResult<string?>(null);
    }

    public ValueTask<List<RadiruStationEntry>> GetRadiruStationsFromAreaServicesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(RadiruStationsFromAreaServices);

    public ValueTask UpsertRadiruAreasAndServicesAsync(
        IEnumerable<NhkRadiruArea> areas,
        IEnumerable<NhkRadiruAreaService> services,
        CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<List<(string AreaId, string ServiceId)>> GetActiveRadiruAreaServiceKeysAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ActiveRadiruAreaServiceKeys);

    public ValueTask<NhkRadiruArea?> GetRadiruAreaByAreaIdAsync(string areaId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(RadiruArea);
}
