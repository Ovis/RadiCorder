using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// らじるAPIクライアントのテスト用スタブ
/// </summary>
public class FakeRadiruApiClient : IRadiruApiClient
{
    public List<RadiruProgramJsonEntity> Programs { get; set; } = [];
    public List<(string AreaId, string ServiceId)> AreaServices { get; set; } = [];
    public List<(string AreaId, string ServiceId, DateTimeOffset Date)> Requests { get; } = [];

    public ValueTask<List<(string AreaId, string ServiceId)>> GetAvailableAreaServicesAsync(
        DateTimeOffset targetDateJst,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(AreaServices);

    public Task<List<RadiruProgramJsonEntity>> GetDailyProgramsAsync(
        string areaId,
        string serviceId,
        DateTimeOffset date,
        CancellationToken cancellationToken = default)
    {
        Requests.Add((areaId, serviceId, date));
        return Task.FromResult(Programs);
    }
}
