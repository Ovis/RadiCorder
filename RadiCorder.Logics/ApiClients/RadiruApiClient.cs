using System.Text.RegularExpressions;
using System.Text.Json;
using RadiCorder.Logics.Errors;
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
                throw new DomainException("らじる★らじる番組表URLが見つかりません。");
            }

            var url = dailyProgramApiUrlTemplate
                .ToHttpsUrl()
                .Replace("{area}", areaId)
                .Replace("{service}", serviceId)
                .Replace("[YYYY-MM-DD]", date.ToString("yyyy-MM-dd"));

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
                cancellationToken,
                beforeAttempt: WaitForRadiruRequestSlotAsync);

            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(jsonString, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.EnumerateObject().Any(x => x.Value.ValueKind == JsonValueKind.Object &&
                    x.Value.TryGetProperty("publication", out var publication) && publication.ValueKind == JsonValueKind.Array))
            {
                throw new DomainException("らじる★らじる番組表の応答形式が不正です。");
            }
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

            var expectedCount = document.RootElement.EnumerateObject()
                .Where(x => x.Value.ValueKind == JsonValueKind.Object && x.Value.TryGetProperty("publication", out var value) && value.ValueKind == JsonValueKind.Array)
                .Sum(x => x.Value.GetProperty("publication").GetArrayLength());
            if (programList.Count != expectedCount)
            {
                throw new DomainException("らじる★らじる番組表の一部を解析できないため更新を中止しました。");
            }

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"らじる★らじる API呼び出し中に例外が発生: エリア {areaId}, 放送局 {serviceId}, 日付 {date:yyyy-MM-dd}");
            throw new DomainException("らじる★らじる番組表の取得に失敗しました。既存データを保持します。", ex);
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
