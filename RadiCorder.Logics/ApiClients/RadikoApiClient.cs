using RadiCorder.Logics.Providers.Radiko;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Web;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.ApiClients;

/// <summary>
/// radikoのAPIクライアント実装
/// </summary>
public class RadikoApiClient(
    ILogger<RadikoApiClient> logger,
    IAppConfigurationService config,
    IHttpClientFactory httpClientFactory) : IRadikoApiClient
{
    private HttpClient HttpClient => httpClientFactory.CreateClient(HttpClientNames.Radiko);

    /// <summary>
    /// Radikoの放送局情報をXMLから取得
    /// </summary>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>放送局情報のリスト</returns>
    public async Task<List<RadikoStation>> GetRadikoStationsAsync(CancellationToken cancellationToken = default)
    {
        var xmlUrl = "http://radiko.jp/v3/station/region/full.xml";
        var radikoStationList = new List<RadikoStation>();

        try
        {
            using var response = await HttpClientExecutionHelper.SendWithRetryAsync(
                logger,
                HttpClient,
                "radiko放送局一覧取得",
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, xmlUrl);
                    request.Headers.Add("Accept-Encoding", "gzip");
                    return request;
                },
                config.ExternalServiceUserAgent,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            var regionOrder = 1;

            // 放送局一覧
            foreach (var stations in doc.Descendants("stations"))
            {
                var regionId = stations.Attribute("region_id")?.Value ?? "";
                var regionName = stations.Attribute("region_name")?.Value ?? "";

                var stationOrder = 0;

                foreach (var station in stations.Descendants("station"))
                {
                    stationOrder++;
                    radikoStationList.Add(new RadikoStation
                    {
                        StationId = station.Descendants("id").FirstOrDefault()?.Value ?? string.Empty,
                        RegionId = regionId,
                        RegionName = regionName,
                        RegionOrder = regionOrder,
                        LogoPath = station.Descendants("logo").FirstOrDefault()?.Value ?? string.Empty,
                        StationName = station.Descendants("name").FirstOrDefault()?.Value.ToSafeName().To半角英数字() ?? string.Empty,
                        Area = station.Descendants("area_id").FirstOrDefault()?.Value ?? string.Empty,
                        StationUrl = station.Descendants("href").FirstOrDefault()?.Value ?? string.Empty,
                        AreaFree = station.Descendants("areafree").FirstOrDefault()?.Value == "1",
                        TimeFree = station.Descendants("timefree").FirstOrDefault()?.Value == "1",
                        StationOrder = stationOrder
                    });
                }

                regionOrder++;
            }

            return radikoStationList;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            logger.ZLogError(e, $"radiko放送局情報取得処理で例外発生");
            throw new DomainException("radiko放送局情報の取得に失敗しました。", e);
        }
    }


    /// <summary>
    /// 指定されたエリアに対応する放送局IDのリストを取得
    /// </summary>
    /// <param name="area">エリアコード</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>放送局IDのリスト</returns>
    public async Task<List<string>> GetStationsByAreaAsync(string area, CancellationToken cancellationToken = default)
    {
        try
        {
            var xmlUrl = $"https://radiko.jp/v3/station/list/{area}.xml";

            using var response = await HttpClientExecutionHelper.SendWithRetryAsync(
                logger,
                HttpClient,
                "radikoエリア放送局一覧取得",
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, xmlUrl);
                    request.Headers.Add("Accept-Encoding", "gzip");
                    return request;
                },
                config.ExternalServiceUserAgent,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);

            var list = new List<string>();

            // 放送局一覧
            foreach (var stations in doc.Descendants("stations"))
            {
                list.AddRange(stations.Descendants("station")
                    .Select(station => station.Descendants("id").FirstOrDefault()?.Value ?? string.Empty));
            }

            return list;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            logger.ZLogError(e, $"エリア {area} の放送局情報取得に失敗");
            throw new DomainException("radiko放送局情報の取得に失敗しました。", e);
        }
    }


    /// <summary>
    /// 指定の放送局の週間番組表を取得する
    /// </summary>
    /// <param name="stationId">放送局ID</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>番組リスト</returns>
    public async Task<List<RadikoProgram>> GetWeeklyProgramsAsync(string stationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var xmlUrl = $"http://radiko.jp/v3/program/station/weekly/{stationId}.xml";

            using var response = await HttpClientExecutionHelper.SendWithRetryAsync(
                logger,
                HttpClient,
                "radiko週間番組表取得",
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, xmlUrl);
                    request.Headers.Add("Accept-Encoding", "gzip");
                    return request;
                },
                config.ExternalServiceUserAgent,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var xmlString = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = XDocument.Parse(xmlString);

            var stations = doc.Descendants("station").ToList();
            if (stations.Count == 0 || stations.Any(x => x.Attribute("id")?.Value != stationId))
            {
                throw new DomainException("radiko番組表の対象局が見つかりません。APIの応答形式を確認してください。");
            }

            var programList = new List<RadikoProgram>();

            // XMLデータをパースして番組リストを作成
            foreach (var programElement in stations.SelectMany(x => x.Descendants("prog")))
            {
                var (program, usedFallback, fallbackFields, strictError) = RadikoProgramParser.ParseProgram(programElement, stationId);
                if (program != null)
                {
                    if (string.IsNullOrWhiteSpace(program.Title) || program.EndTime <= program.StartTime)
                    {
                        throw new DomainException("radiko番組表の必須項目が不正です。");
                    }
                    programList.Add(program);
                }
                else
                {
                    throw new DomainException("radiko番組表を解析できないため更新を中止しました。", strictError);
                }

                if (usedFallback)
                {
                    var context = RadikoProgramParser.BuildProgramContext(programElement);
                    var fieldText = fallbackFields.Count == 0
                        ? "unknown"
                        : string.Join(",", fallbackFields);

                    if (program != null)
                    {
                        logger.ZLogWarning(strictError, $"番組データのstrict解析に失敗したためフォールバックで救済: StationId={stationId}, Fields={fieldText}, Context={context}");
                    }
                    else
                    {
                        logger.ZLogWarning(strictError, $"番組データの解析に失敗しスキップ: StationId={stationId}, Fields={fieldText}, Context={context}");
                    }
                }
            }

            return programList;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"radiko API呼び出し中に例外が発生: StationId={stationId}");
            throw new DomainException("radiko番組表の取得に失敗しました。既存データを保持します。", ex);
        }
    }

    /// <summary>
    /// リアルタイム再生・録音用のplaylist_create_urlを取得する
    /// </summary>
    /// <param name="stationId">放送局ID</param>
    /// <param name="useAreaFreeConnection">エリアフリー接続を利用するか</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>リアルタイム再生・録音用URLのリスト</returns>
    public async Task<List<string>> GetRealTimePlaylistUrlsAsync(
        string stationId,
        bool useAreaFreeConnection,
        string? requestStationId = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrls = await GetPlaylistCreateUrlsByFlagsAsync(stationId, useAreaFreeConnection, isTimeFree: false, cancellationToken);
        var effectiveStationId = string.IsNullOrWhiteSpace(requestStationId) ? stationId : requestStationId;
        return baseUrls
            .Select(x => BuildRealTimePlaylistUrl(x, effectiveStationId, useAreaFreeConnection))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// タイムフリー録音用のplaylist_create_urlを取得する
    /// </summary>
    /// <param name="stationId">放送局ID</param>
    /// <param name="useAreaFreeConnection">エリアフリー接続を利用するか</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>playlist_create_urlのリスト</returns>
    public async Task<List<string>> GetTimeFreePlaylistCreateUrlsAsync(
        string stationId,
        bool useAreaFreeConnection,
        CancellationToken cancellationToken = default)
    {
        return await GetPlaylistCreateUrlsByFlagsAsync(stationId, useAreaFreeConnection, isTimeFree: true, cancellationToken);
    }

    private async Task<List<string>> GetPlaylistCreateUrlsByFlagsAsync(
        string stationId,
        bool useAreaFreeConnection,
        bool isTimeFree,
        CancellationToken cancellationToken)
    {
        try
        {
            var areafree = useAreaFreeConnection ? "1" : "0";
            var timefree = isTimeFree ? "1" : "0";
            var xmlUrl = $"https://radiko.jp/v3/station/stream/pc_html5/{stationId}.xml";

            using var response = await HttpClientExecutionHelper.SendWithRetryAsync(
                logger,
                HttpClient,
                isTimeFree ? "radikoタイムフリーURL取得" : "radikoリアルタイムURL取得",
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, xmlUrl);
                    request.Headers.Add("Accept-Encoding", "gzip");
                    return request;
                },
                config.ExternalServiceUserAgent,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var xmlString = await response.Content.ReadAsStringAsync(cancellationToken);

            var doc = XDocument.Parse(xmlString);
            var nodes = doc.XPathSelectElements($"/urls/url[@timefree='{timefree}' and @areafree='{areafree}']/playlist_create_url");
            var list = nodes.Select(x => x.Value.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            return list;
        }
        catch (Exception ex)
        {
            var kind = isTimeFree ? "タイムフリー" : "リアルタイム";
            logger.ZLogError(ex, $"radiko{kind}URL取得に失敗: StationId={stationId}");
            throw new DomainException($"radiko{kind}URLの取得に失敗しました。", ex);
        }
    }

    private static string BuildRealTimePlaylistUrl(string baseUrl, string stationId, bool useAreaFreeConnection)
    {
        var builder = new UriBuilder(baseUrl);
        var query = HttpUtility.ParseQueryString(builder.Query);
        query["station_id"] = stationId;
        query["l"] = "15";
        query["lsid"] = Guid.NewGuid().ToString("N");
        query["type"] = useAreaFreeConnection ? "c" : "b";
        builder.Query = query.ToString() ?? string.Empty;
        return builder.Uri.ToString();
    }
}

