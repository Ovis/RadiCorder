using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// radiko APIクライアントのテスト用スタブ
/// </summary>
public class FakeRadikoApiClient : IRadikoApiClient
{
    public List<RadikoStation> Stations { get; set; } = [];
    public List<string> StationsByArea { get; set; } = [];
    public List<RadikoProgram> WeeklyPrograms { get; set; } = [];
    public List<string> RealTimeUrls { get; set; } = [];
    public List<string> RealTimeUrlsForAreaFree { get; set; } = [];
    public List<string> TimeFreeUrls { get; set; } = [];
    public List<string> TimeFreeUrlsForAreaFree { get; set; } = [];
    public string? LastRealTimeRequestStationId { get; private set; }

    public Task<List<RadikoStation>> GetRadikoStationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Stations);
    }

    public Task<List<string>> GetStationsByAreaAsync(string area, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(StationsByArea);
    }

    public Task<List<RadikoProgram>> GetWeeklyProgramsAsync(string stationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(WeeklyPrograms);
    }

    public Task<List<string>> GetRealTimePlaylistUrlsAsync(string stationId, bool useAreaFreeConnection, string? requestStationId = null, CancellationToken cancellationToken = default)
    {
        LastRealTimeRequestStationId = requestStationId ?? stationId;
        var list = useAreaFreeConnection && RealTimeUrlsForAreaFree.Count != 0
            ? RealTimeUrlsForAreaFree
            : RealTimeUrls;
        return Task.FromResult(list);
    }

    public Task<List<string>> GetTimeFreePlaylistCreateUrlsAsync(string stationId, bool useAreaFreeConnection, CancellationToken cancellationToken = default)
    {
        var list = useAreaFreeConnection && TimeFreeUrlsForAreaFree.Count != 0
            ? TimeFreeUrlsForAreaFree
            : TimeFreeUrls;
        return Task.FromResult(list);
    }
}
