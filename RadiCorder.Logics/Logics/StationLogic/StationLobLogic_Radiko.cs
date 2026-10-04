using Microsoft.Extensions.Caching.Memory;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Logics.StationLogic
{
    public partial class StationLobLogic
    {
        /// <summary>
        /// radikoの放送局情報が初期化されているかチェック
        /// </summary>
        /// <returns></returns>
        public async ValueTask<bool> CheckInitializedRadikoStationAsync()
        {
            try
            {
                var hasStationData =
                    await stationRepository.HasAnyRadikoStationAsync();

                return hasStationData;
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"radiko放送局情報初期化状況チェックに失敗");
                throw;
            }
        }



        public async ValueTask UpsertRadikoStationDefinitionAsync(CancellationToken cancellationToken = default)
        {
            // クライアントから放送局情報を取得
            var radikoStationList = await radikoApiClient.GetRadikoStationsAsync(cancellationToken);
            if (radikoStationList.Count == 0)
            {
                throw new DomainException("radiko放送局情報が空のため同期を中止しました。");
            }

            try
            {
                await stationRepository.UpsertRadikoStationsAsync(radikoStationList, cancellationToken);

                // 放送局情報をキャッシュに保持
                config.UpdateRadikoStationDic(radikoStationList);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"radikoの放送局情報登録処理に失敗");
                throw;
            }
        }



        public async ValueTask<List<RadikoStation>> GetAllRadikoStationAsync(bool activeOnly = true)
        {
            try
            {
                return await stationRepository.GetRadikoStationsAsync(activeOnly);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"radiko放送局情報取得処理に失敗");
                throw;
            }
        }



        public async ValueTask<IEnumerable<RadikoStationInformationEntry>> GetRadikoStationAsync()
        {
            var (isSuccess, area) = await radikoUniqueProcessLogic.GetRadikoAreaAsync();

            if (!isSuccess)
            {
                return [];
            }

            var stations = await stationRepository.GetRadikoStationsAsync();
            var list = stations.Select(s => entryMapper.ToRadikoStationInformationEntry(s)).ToList();

            // エリアフリー利用時はエリアを問わず放送局を返す
            if (config.IsRadikoAreaFree)
            {
                // エリアフリーに対応しているか、視聴可能エリアが同一の放送局のみ返す
                return list.Where(r => r.AreaFree || r.Area == area);
            }

            var currentAreaStations = await GetCurrentAreaStations(area);

            return list.Where(r => currentAreaStations.Contains(r.StationId));
        }



        /// <summary>
        /// 現在のエリアに対応する放送局一覧を取得しキャッシュ保持する
        /// </summary>
        /// <param name="area"></param>
        /// <returns></returns>
        public async ValueTask<List<string>> GetCurrentAreaStations(string area)
        {
            var key = $"currentAreaStations_{area}";

            var list = await Cache.GetOrCreateAsync(
                key: key,
                factory: async entry =>
                {
                    var stationList = await radikoApiClient.GetStationsByAreaAsync(area);


                    entry.AbsoluteExpirationRelativeToNow = stationList.Count == 0
                        ? TimeSpan.FromTicks(1)
                        : AbsoluteExpirationRelativeToNow;

                    logger.ZLogInformation($"現在エリアの放送局一覧キャッシュ完了");

                    return stationList;
                });

            return list ?? [];
        }
    }
}

