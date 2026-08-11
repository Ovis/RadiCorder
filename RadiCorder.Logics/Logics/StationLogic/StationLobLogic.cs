using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Domain.Station;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Logics.StationLogic
{
    public partial class StationLobLogic(
        ILogger<StationLobLogic> logger,
        IRadioAppContext appContext,
        IAppConfigurationService config,
        IRadikoApiClient radikoApiClient,
        IStationRepository stationRepository,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic,
        IHttpClientFactory httpClientFactory,
        IEntryMapper entryMapper)
    {
        private static readonly IMemoryCache Cache =
            new MemoryCache(new MemoryCacheOptions
            {
                ExpirationScanFrequency = TimeSpan.FromSeconds(60),
            });

        private static readonly TimeSpan AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
    }
}

