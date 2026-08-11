using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Domain.Station;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Infrastructure.Station;

/// <summary>
/// 放送局情報の永続化を担うリポジトリ実装
/// </summary>
public class StationRepository(RadioDbContext dbContext) : IStationRepository
{
    /// <summary>
    /// radiko放送局が初期化済みか確認する
    /// </summary>
    public async ValueTask<bool> HasAnyRadikoStationAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.RadikoStations
            .AsNoTracking()
            .Where(x => x.IsActive)
            .AnyAsync(cancellationToken);
    }

    /// <summary>
    /// radiko放送局を取得する
    /// </summary>
    public async ValueTask<List<RadikoStation>> GetRadikoStationsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var query = dbContext.RadikoStations.AsQueryable();
            if (activeOnly)
            {
                query = query.Where(x => x.IsActive);
            }

            var list = await query
                .ToListAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return list;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// radiko放送局を追加する
    /// </summary>
    public async ValueTask UpsertRadikoStationsAsync(IEnumerable<RadikoStation> stations, CancellationToken cancellationToken = default)
    {
        var syncedAtUtc = DateTimeOffset.UtcNow;
        var stationList = stations
            .Where(s => !string.IsNullOrWhiteSpace(s.StationId))
            .GroupBy(s => s.StationId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (stationList.Count == 0)
        {
            return;
        }

        var stationIdSet = stationList
            .Select(s => s.StationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingStations = await dbContext.RadikoStations
                .ToListAsync(cancellationToken);
            var existingById = existingStations
                .ToDictionary(x => x.StationId, StringComparer.OrdinalIgnoreCase);

            foreach (var station in stationList)
            {
                if (!existingById.TryGetValue(station.StationId, out var existing))
                {
                    station.IsActive = true;
                    station.LastSeenAtUtc = syncedAtUtc;
                    await dbContext.RadikoStations.AddAsync(station, cancellationToken);
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
                existing.LastSeenAtUtc = syncedAtUtc;
            }

            foreach (var existing in existingStations)
            {
                if (!stationIdSet.Contains(existing.StationId))
                {
                    existing.IsActive = false;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// らじる★らじる放送局が初期化済みか確認する
    /// </summary>
    public async ValueTask<bool> HasAnyRadiruStationAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var hasStationData = await dbContext.NhkRadiruAreaServices
                .AsNoTracking()
                .Where(x => x.IsActive)
                .AnyAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return hasStationData;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// 指定エリアとサービスIDに対応するらじる★らじるHLS URLを取得する
    /// </summary>
    public async ValueTask<string?> GetRadiruHlsUrlByAreaAndServiceAsync(
        string areaId,
        string serviceId,
        CancellationToken cancellationToken = default)
    {
        var normalizedServiceId = serviceId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(areaId) || string.IsNullOrWhiteSpace(normalizedServiceId))
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var serviceUrl = await dbContext.NhkRadiruAreaServices
                .AsNoTracking()
                .Where(s => s.AreaId == areaId && s.IsActive)
                .Where(s => s.ServiceId.ToLower() == normalizedServiceId)
                .Select(s => s.HlsUrl)
                .FirstOrDefaultAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(serviceUrl) ? null : serviceUrl;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// らじる★らじる局一覧を取得する
    /// </summary>
    public async ValueTask<List<RadiruStationEntry>> GetRadiruStationsFromAreaServicesAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var list = await (
                    from area in dbContext.NhkRadiruAreas.AsNoTracking()
                    join service in dbContext.NhkRadiruAreaServices.AsNoTracking()
                        on area.AreaId equals service.AreaId
                    where service.IsActive
                    orderby area.AreaId, service.ServiceId
                    select new RadiruStationEntry
                    {
                        AreaId = area.AreaId,
                        AreaName = area.AreaJpName,
                        StationId = service.ServiceId,
                        StationName = service.ServiceName
                    })
                .ToListAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return list;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// らじる★らじるのエリア定義とサービス定義を追加または更新する
    /// </summary>
    public async ValueTask UpsertRadiruAreasAndServicesAsync(
        IEnumerable<NhkRadiruArea> areas,
        IEnumerable<NhkRadiruAreaService> services,
        CancellationToken cancellationToken = default)
    {
        var areaList = areas
            .Where(a => !string.IsNullOrWhiteSpace(a.AreaId))
            .GroupBy(a => a.AreaId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var areaIdSet = areaList
            .Select(a => a.AreaId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var serviceList = services
            .Where(s => !string.IsNullOrWhiteSpace(s.AreaId))
            .Where(s => !string.IsNullOrWhiteSpace(s.ServiceId))
            .Where(s => !string.IsNullOrWhiteSpace(s.HlsUrl))
            .Where(s => areaIdSet.Contains(s.AreaId))
            .GroupBy(s => $"{s.AreaId}:{s.ServiceId}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var area in areaList)
            {
                var existing = await dbContext.NhkRadiruAreas
                    .Where(a => a.AreaId == area.AreaId)
                    .SingleOrDefaultAsync(cancellationToken);

                if (existing == null)
                {
                    await dbContext.NhkRadiruAreas.AddAsync(area, cancellationToken);
                }
                else
                {
                    existing.AreaJpName = area.AreaJpName;
                    existing.ApiKey = area.ApiKey;
                    existing.ProgramNowOnAirApiUrl = area.ProgramNowOnAirApiUrl;
                    existing.ProgramDetailApiUrlTemplate = area.ProgramDetailApiUrlTemplate;
                    existing.DailyProgramApiUrlTemplate = area.DailyProgramApiUrlTemplate;
                    existing.LastSyncedAtUtc = area.LastSyncedAtUtc;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (areaIdSet.Count > 0)
            {
                var oldServices = await dbContext.NhkRadiruAreaServices
                    .Where(s => areaIdSet.Contains(s.AreaId))
                    .ToListAsync(cancellationToken);

                if (oldServices.Count > 0)
                {
                    dbContext.NhkRadiruAreaServices.RemoveRange(oldServices);
                }
            }

            if (serviceList.Count > 0)
            {
                await dbContext.NhkRadiruAreaServices.AddRangeAsync(serviceList, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// らじる★らじるの有効なエリアID/サービスID組を取得する
    /// </summary>
    public async ValueTask<List<(string AreaId, string ServiceId)>> GetActiveRadiruAreaServiceKeysAsync(CancellationToken cancellationToken = default)
    {
        var keys = await dbContext.NhkRadiruAreaServices
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.AreaId, x.ServiceId })
            .Distinct()
            .ToListAsync(cancellationToken);

        return keys
            .Select(x => (x.AreaId, x.ServiceId))
            .ToList();
    }

    /// <summary>
    /// 指定エリアIDの定義を取得する
    /// </summary>
    public async ValueTask<NhkRadiruArea?> GetRadiruAreaByAreaIdAsync(string areaId, CancellationToken cancellationToken = default)
    {
        return await dbContext.NhkRadiruAreas
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.AreaId == areaId, cancellationToken);
    }
}
