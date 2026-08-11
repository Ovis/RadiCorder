using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.StationLogic;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.ApiClients;

public class RadiruApiClient(
    ILogger<RadiruApiClient> logger,
    StationLobLogic stationLobLogic,
    IAppConfigurationService config,
    IHttpClientFactory httpClientFactory
) : IRadiruApiClient
{
    private static readonly SemaphoreSlim RequestPacingLock = new(1, 1);
    private static DateTimeOffset _nextAllowedRequestUtc = DateTimeOffset.MinValue;

    private HttpClient HttpClient => httpClientFactory.CreateClient(HttpClientNames.Radiru);

    /// <summary>
    /// 取得対象のエリアID/サービスID組一覧を取得
    /// </summary>
    public ValueTask<List<(string AreaId, string ServiceId)>> GetAvailableAreaServicesAsync(
        DateTimeOffset targetDateJst,
        CancellationToken cancellationToken = default)
        => stationLobLogic.GetActiveRadiruAreaServiceKeysAsync(targetDateJst, cancellationToken);

    /// <summary>
    ///  指定されたエリアID、サービスID、日付の番組表を取得する
    /// </summary>
    public async Task<List<RadiruProgramJsonEntity>> GetDailyProgramsAsync(
        string areaId,
        string serviceId,
        DateTimeOffset date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var dailyProgramApiUrlTemplate = await stationLobLogic.GetRadiruDailyProgramApiUrlTemplateAsync(areaId, cancellationToken);
            if (string.IsNullOrWhiteSpace(dailyProgramApiUrlTemplate))
            {
                logger.ZLogWarning($"らじる★らじる番組表URLテンプレートが見つからないためスキップ areaId={areaId} serviceId={serviceId}");
                return [];
            }

            var url = dailyProgramApiUrlTemplate
                .ToHttpsUrl()
                .Replace("{area}", areaId)
                .Replace("{service}", serviceId)
                .Replace("[YYYY-MM-DD]", date.ToString("yyyy-MM-dd"));

            await WaitForRadiruRequestSlotAsync(cancellationToken);

            using var response = await HttpClientExecutionHelper.SendWithRetryAsync(
                logger,
                HttpClient,
                "Radiru API呼び出し",
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Accept-Encoding", "gzip");
                    return request;
                },
                config.ExternalServiceUserAgent,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.ZLogError($"らじる★らじる API呼び出しに失敗: {url}, StatusCode: {response.StatusCode.ToString()}");
                return [];
            }

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            var errorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var programList = RadiruProgramJsonEntity.FromJson(
                jsonString,
                onError: tuple =>
                {
                    var path = ExtractJsonPath(tuple.ex.Message);
                    if (!errorCounts.TryAdd(path, 1))
                    {
                        errorCounts[path]++;
                    }
                });

            if (errorCounts.Count > 0)
            {
                var summary = string.Join(", ",
                    errorCounts
                        .OrderByDescending(x => x.Value)
                        .Take(5)
                        .Select(x => $"{x.Key}:{x.Value}"));

                logger.ZLogWarning($"らじる★らじる JSONデシリアライズでフォールバックが発生 path/count={summary}");
            }

            return programList;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"らじる★らじる API呼び出し中に例外が発生: エリア {areaId}, 放送局 {serviceId}, 日付 {date:yyyy-MM-dd}");
            return [];
        }
    }

    private static string ExtractJsonPath(string message)
    {
        var match = Regex.Match(message, @"Path:\s*(?<path>\$[^|]*)\s*\|", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            return match.Groups["path"].Value.Trim();
        }

        return "unknown";
    }

    private async ValueTask WaitForRadiruRequestSlotAsync(CancellationToken cancellationToken)
    {
        var minIntervalMs = Math.Max(0, config.RadiruApiMinRequestIntervalMs);
        var jitterMs = Math.Max(0, config.RadiruApiRequestJitterMs);

        if (minIntervalMs == 0 && jitterMs == 0)
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var waitUntilUtc = nowUtc;

        await RequestPacingLock.WaitAsync(cancellationToken);
        try
        {
            if (_nextAllowedRequestUtc > nowUtc)
            {
                waitUntilUtc = _nextAllowedRequestUtc;
            }

            var intervalWithJitterMs = minIntervalMs;
            if (jitterMs > 0)
            {
                intervalWithJitterMs += Random.Shared.Next(0, jitterMs + 1);
            }

            _nextAllowedRequestUtc = (waitUntilUtc > nowUtc ? waitUntilUtc : nowUtc)
                .AddMilliseconds(intervalWithJitterMs);
        }
        finally
        {
            RequestPacingLock.Release();
        }

        var delay = waitUntilUtc - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken);
        }
    }
}
