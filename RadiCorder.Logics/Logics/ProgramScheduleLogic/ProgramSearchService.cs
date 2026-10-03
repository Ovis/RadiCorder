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
    IEntryMapper entryMapper)
{
    public async ValueTask<List<ProgramForApiEntry>> SearchAsync(ProgramSearchEntity entity)
    {
        var radikoResults = new List<RadikoProgram>();
        var radiruResults = new List<NhkRadiruProgram>();

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
                radikoResults = await programScheduleLobLogic.SearchRadikoProgramAsync(entity);
            }
        }

        if (entity.SelectedRadiruStationIds.Any())
        {
            var visibleRadiruStationIds = (await stationLobLogic.GetRadiruStationAsync())
                .Select(x => $"{x.AreaId}:{x.StationId}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            entity.SelectedRadiruStationIds = entity.SelectedRadiruStationIds
                .Where(id => visibleRadiruStationIds.Contains(id))
                .ToList();

            if (entity.SelectedRadiruStationIds.Any())
            {
                radiruResults = await programScheduleLobLogic.SearchRadiruProgramAsync(entity);
            }
        }

        return ProgramSearchResultBuilder.Build(radikoResults, radiruResults, entity.OrderKind, entryMapper);
    }

    /// <summary>
    /// 現在エリアのradiko放送局ID一覧を取得する。
    /// </summary>
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
