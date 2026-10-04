using System.Xml.Linq;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Logics.StationLogic
{
    public partial class StationLobLogic
    {
        /// <summary>
        /// らじる★らじるの放送局情報が初期化されているかチェック
        /// </summary>
        /// <returns></returns>
        public async ValueTask<bool> CheckInitializedRadiruRadiruStationAsync()
        {
            try
            {
                var hasStationData = await stationRepository.HasAnyRadiruStationAsync();

                return hasStationData;
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"Failed to check if radiru station definitions are initialized.");
                throw;
            }
        }

        /// <summary>
        /// らじる★らじるの放送局情報取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<IEnumerable<RadiruStationEntry>> GetRadiruStationAsync(CancellationToken cancellationToken = default)
        {
            var stationEntries = await stationRepository.GetRadiruStationsFromAreaServicesAsync(cancellationToken);
            return stationEntries
                .Where(entry => IsRadiruStationVisible(entry.StationId, appContext.StandardDateTimeOffset))
                .Select(entry => new RadiruStationEntry
                {
                    AreaId = entry.AreaId,
                    AreaName = entry.AreaName,
                    StationId = entry.StationId,
                    StationName = ResolveRadiruStationName(entry.StationId)
                })
                .GroupBy(x => $"{x.AreaId}:{x.StationId}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }


        /// <summary>
        /// らじる★らじるの放送局情報を更新
        /// </summary>
        /// <returns></returns>
        public async ValueTask<bool> UpdateRadiruStationInformationAsync(CancellationToken cancellationToken = default)
        {
            List<NhkRadiruArea> areaDefinitions;
            List<NhkRadiruAreaService> serviceDefinitions;
            try
            {
                using var client = httpClientFactory.CreateClient(HttpClientNames.Radiru);
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.nhk.or.jp/radio/config/config_web.xml");
                request.Headers.Accept.ParseAdd("application/xml");
                request.Headers.AcceptLanguage.ParseAdd("ja-JP,ja;q=0.9,en;q=0.8");
                request.Headers.TryAddWithoutValidation("User-Agent", config.ExternalServiceUserAgent);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    logger.ZLogError($"らじる★らじるの設定XML取得に失敗: StatusCode={response.StatusCode}");
                    response.EnsureSuccessStatusCode();
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var doc = await XDocument.LoadAsync(responseStream, LoadOptions.None, cancellationToken);

                var programNowOnAirUrlTemplate = GetDescendantValue(doc, "url_program_noa");
                var programDetailApiUrlTemplate = GetDescendantValue(doc, "url_program_detail");
                var dailyProgramApiUrlTemplate = GetDescendantValue(doc, "url_program_day");
                var syncedAtUtc = DateTimeOffset.UtcNow;

                areaDefinitions = [];
                serviceDefinitions = [];

                foreach (var data in doc.Descendants("stream_url").Descendants("data"))
                {
                    var areaId = GetDescendantValue(data, "areakey");
                    if (string.IsNullOrWhiteSpace(areaId))
                    {
                        logger.ZLogWarning($"らじる★らじる設定XMLで areakey が空のためスキップしました。");
                        throw new RadiCorder.Logics.Errors.DomainException("らじるの局定義でエリアIDが欠落しています。既存DBを保持します。");
                    }

                    var areaName = GetDescendantValue(data, "areajp");
                    var apiKey = GetDescendantValue(data, "apikey");
                    var areaNoaUrl = programNowOnAirUrlTemplate.Replace("{area}", areaId).ToHttpsUrl();
                    var areaDetailUrl = programDetailApiUrlTemplate.Replace("{area}", areaId).ToHttpsUrl();
                    var areaDailyUrl = dailyProgramApiUrlTemplate.Replace("{area}", areaId).ToHttpsUrl();

                    var serviceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var element in data.Elements())
                    {
                        var tagName = element.Name.LocalName;
                        if (!tagName.EndsWith("hls", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var serviceId = ConvertHlsTagNameToServiceId(tagName);
                        var hlsUrl = (element.Value ?? string.Empty).Trim().ToHttpsUrl();
                        if (string.IsNullOrWhiteSpace(serviceId) || string.IsNullOrWhiteSpace(hlsUrl))
                        {
                            continue;
                        }

                        serviceMap[serviceId] = hlsUrl;

                        serviceDefinitions.Add(new NhkRadiruAreaService
                        {
                            AreaId = areaId,
                            ServiceId = serviceId,
                            ServiceName = ResolveRadiruStationName(serviceId),
                            HlsUrl = hlsUrl,
                            IsActive = true,
                            SourceTag = tagName.ToLowerInvariant(),
                            LastSyncedAtUtc = syncedAtUtc
                        });
                    }

                    if (serviceMap.Count == 0)
                    {
                        logger.ZLogWarning($"らじる★らじる設定XMLでHLSサービスが取得できませんでした。 areaId={areaId}");
                    }

                    areaDefinitions.Add(new NhkRadiruArea
                    {
                        AreaId = areaId,
                        AreaJpName = areaName,
                        ApiKey = apiKey,
                        ProgramNowOnAirApiUrl = areaNoaUrl,
                        ProgramDetailApiUrlTemplate = areaDetailUrl,
                        DailyProgramApiUrlTemplate = areaDailyUrl,
                        LastSyncedAtUtc = syncedAtUtc
                    });
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"らじる\u2605らじるの放送局情報取得処理で例外発生");
                throw;
            }


            try
            {
                await stationRepository.UpsertRadiruAreasAndServicesAsync(areaDefinitions, serviceDefinitions, cancellationToken);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"らじる★らじるの放送局情報更新に失敗");
                throw;
            }

            return true;
        }

        /// <summary>
        /// らじる★らじるの放送局定義を、未確認日であれば再取得する
        /// </summary>
        public async ValueTask<bool> TryUpdateRadiruStationInformationIfDueAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await UpdateRadiruStationInformationIfDueAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                logger.ZLogWarning(e, $"らじる★らじるの放送局定義更新に失敗しました。既存データで継続します。");
                return false;
            }
        }

        public async ValueTask<bool> UpdateRadiruStationInformationIfDueAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lastCheckedAt = await config.GetRadiruStationDefinitionLastCheckedAtAsync();
            var todayJst = DateOnly.FromDateTime(appContext.StandardDateTimeOffset.ToJapanDateTime());

            if (lastCheckedAt.HasValue)
            {
                var checkedDateJst = DateOnly.FromDateTime(lastCheckedAt.Value.ToJapanDateTime());
                if (checkedDateJst >= todayJst)
                {
                    return false;
                }
            }

            await UpdateRadiruStationInformationAsync(cancellationToken);
            await config.UpdateRadiruStationDefinitionLastCheckedAtAsync(appContext.StandardDateTimeOffset.ToUniversalTime());
            return true;
        }


        /// <summary>
        /// 指定エリアとサービスIDかららじる★らじるのHLS URLを取得
        /// </summary>
        /// <param name="areaId">エリアID</param>
        /// <param name="serviceId">サービスID</param>
        /// <param name="cancellationToken">キャンセル用トークン</param>
        /// <returns>HLS URL。未取得時はnull</returns>
        public async ValueTask<string?> GetRadiruHlsUrlByAreaAndServiceAsync(
            string areaId,
            string serviceId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await stationRepository.GetRadiruHlsUrlByAreaAndServiceAsync(areaId, serviceId, cancellationToken);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"らじる★らじるのHLS URL取得に失敗 areaId={areaId} serviceId={serviceId}");
                throw;
            }
        }

        /// <summary>
        /// らじる★らじるの有効なエリアID/サービスID組を取得
        /// </summary>
        public async ValueTask<List<(string AreaId, string ServiceId)>> GetActiveRadiruAreaServiceKeysAsync(
            DateTimeOffset targetDateJst,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var keys = await stationRepository.GetActiveRadiruAreaServiceKeysAsync(cancellationToken);
                return keys
                    .Where(x => CanFetchRadiruProgramsForDate(x.ServiceId, appContext.StandardDateTimeOffset, targetDateJst))
                    .ToList();
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"らじる★らじるサービス定義の取得に失敗しました。");
                throw;
            }
        }

        /// <summary>
        /// 指定エリアの番組表API URLテンプレートを取得
        /// </summary>
        public async ValueTask<string?> GetRadiruDailyProgramApiUrlTemplateAsync(string areaId, CancellationToken cancellationToken = default)
        {
            try
            {
                var area = await stationRepository.GetRadiruAreaByAreaIdAsync(areaId, cancellationToken);
                if (area != null && !string.IsNullOrWhiteSpace(area.DailyProgramApiUrlTemplate))
                {
                    return area.DailyProgramApiUrlTemplate;
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"らじる★らじるエリア定義取得に失敗 areaId={areaId}");
                throw;
            }
            return null;
        }

        private static string ResolveRadiruStationName(string stationId)
        {
            var station = RadiruStationKind.FindByServiceId(stationId);

            return station?.Name ?? $"不明局({stationId})";
        }

        private static bool IsRadiruStationVisible(string stationId, DateTimeOffset nowJst)
        {
            var station = RadiruStationKind.FindByServiceId(stationId);
            return station?.IsVisibleAt(nowJst) ?? true;
        }

        private static bool CanFetchRadiruProgramsForDate(string stationId, DateTimeOffset nowJst, DateTimeOffset targetDateJst)
        {
            var station = RadiruStationKind.FindByServiceId(stationId);
            return station?.CanFetchProgramsForDateAt(nowJst, targetDateJst) ?? true;
        }

        private static string GetDescendantValue(XContainer element, string descendantName)
        {
            return element.Descendants(descendantName).FirstOrDefault()?.Value?.Trim() ?? string.Empty;
        }

        private static string ConvertHlsTagNameToServiceId(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return string.Empty;
            }

            var normalized = tagName.Trim().ToLowerInvariant();
            if (!normalized.EndsWith("hls", StringComparison.Ordinal) || normalized.Length <= 3)
            {
                return string.Empty;
            }

            var baseId = normalized[..^3];
            if (baseId == "fm")
            {
                return "r3";
            }

            return baseId;
        }

    }
}

