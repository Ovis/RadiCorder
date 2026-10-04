using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Providers.Radiko;

/// <summary>
/// Radikoの検索条件と利用可能局の判定をまとめる。
/// </summary>
public class RadikoProgramSearchProvider(
    ILogger<ProgramSearchService> logger,
    IAppConfigurationService config,
    RadikoUniqueProcessLogic radikoUniqueProcessLogic,
    StationLobLogic stationLobLogic,
    ProgramScheduleLobLogic programScheduleLobLogic,
    IEntryMapper entryMapper) : IProgramSearchProvider
{
    public RadioServiceKind ServiceKind => RadioServiceKind.Radiko;

    public async ValueTask<List<ProgramForApiEntry>> SearchAsync(ProgramSearchEntity filters, IReadOnlyList<string> stationIds)
    {
        var entity = ProgramSearchFilters.Copy(filters);
        entity.SelectedRadikoStationIds = stationIds.ToList();
        if (entity.SelectedRadikoStationIds.Any())
        {
            if (!config.IsRadikoAreaFree)
            {
                var currentAreaStations = await GetCurrentAreaStationsAsync(logger, radikoUniqueProcessLogic, stationLobLogic);
                var currentAreaStationSet = currentAreaStations.ToHashSet(StringComparer.OrdinalIgnoreCase);
                entity.SelectedRadikoStationIds = entity.SelectedRadikoStationIds
                    .Where(id => currentAreaStationSet.Contains(id))
                    .ToList();
            }

            if (entity.SelectedRadikoStationIds.Any())
            {
                return (await programScheduleLobLogic.SearchRadikoProgramAsync(entity)).Select(entryMapper.ToRadikoProgramForApiEntry).ToList();
            }
        }

        return [];
    }

    private static async ValueTask<List<string>> GetCurrentAreaStationsAsync(
        ILogger<ProgramSearchService> logger,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic,
        StationLobLogic stationLobLogic)
    {
        var (isSuccess, area) = await radikoUniqueProcessLogic.GetRadikoAreaAsync();
        if (!isSuccess || string.IsNullOrWhiteSpace(area))
        {
            logger.ZLogWarning($"radikoエリア情報の取得に失敗");
            return [];
        }

        return await stationLobLogic.GetCurrentAreaStations(area);
    }
}
