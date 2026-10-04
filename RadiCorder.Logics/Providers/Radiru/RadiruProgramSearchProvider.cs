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

namespace RadiCorder.Logics.Providers.Radiru;

/// <summary>
/// Radiruの検索条件と利用可能局の判定をまとめる。
/// </summary>
public class RadiruProgramSearchProvider(
    StationLobLogic stationLobLogic,
    ProgramScheduleLobLogic programScheduleLobLogic,
    IEntryMapper entryMapper) : IProgramSearchProvider
{
    public RadioServiceKind ServiceKind => RadioServiceKind.Radiru;

    public async ValueTask<List<ProgramForApiEntry>> SearchAsync(ProgramSearchEntity filters, IReadOnlyList<string> stationIds)
    {
        var entity = ProgramSearchFilters.Copy(filters);
        entity.SelectedRadiruStationIds = stationIds.ToList();
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
                return (await programScheduleLobLogic.SearchRadiruProgramAsync(entity)).Select(entryMapper.ToRadiruProgramForApiEntry).ToList();
            }
        }

        return [];
    }


}
