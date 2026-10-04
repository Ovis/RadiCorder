using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Providers.Radiko;
using RadiCorder.Logics.Providers.Radiru;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.ProgramScheduleLogic;

/// <summary>
/// 利用可能な放送局で検索し、サービス横断の検索結果を返す。
/// </summary>
public class ProgramSearchService(
    ILogger<ProgramSearchService> logger,
    IAppConfigurationService config,
    RadikoUniqueProcessLogic radikoUniqueProcessLogic,
    StationLobLogic stationLobLogic,
    ProgramScheduleLobLogic programScheduleLobLogic,
    IEntryMapper entryMapper,
    IEnumerable<IProgramSearchProvider>? providers = null)
{
    private readonly IReadOnlyDictionary<RadioServiceKind, IProgramSearchProvider> _providers =
        (providers ?? [new RadikoProgramSearchProvider(logger, config, radikoUniqueProcessLogic, stationLobLogic, programScheduleLobLogic, entryMapper),
            new RadiruProgramSearchProvider(stationLobLogic, programScheduleLobLogic, entryMapper)])
        .ToDictionary(x => x.ServiceKind);

    public ValueTask<List<ProgramForApiEntry>> SearchAsync(ProgramSearchEntity entity)
        => SearchAsync(new ProgramSearchRequest(entity, new Dictionary<RadioServiceKind, IReadOnlyList<string>>
        {
            [RadioServiceKind.Radiko] = entity.SelectedRadikoStationIds,
            [RadioServiceKind.Radiru] = entity.SelectedRadiruStationIds
        }));

    public async ValueTask<List<ProgramForApiEntry>> SearchAsync(ProgramSearchRequest request)
    {
        var result = new List<ProgramForApiEntry>();
        foreach (var provider in _providers.Values.OrderBy(x => (int)x.ServiceKind))
        {
            if (request.Stations.TryGetValue(provider.ServiceKind, out var stations) && stations.Count > 0)
                result.AddRange(await provider.SearchAsync(request.Filters, stations));
        }
        return ProgramSearchResultBuilder.Build(result, request.Filters.OrderKind);
    }
}
