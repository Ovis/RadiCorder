using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Application;
using ZLogger;

namespace RadiCorder.Logics.Services.Streaming;

/// <summary>
/// 認証済みradiko配信の取得とライブ開始時刻の同期を担当する。
/// </summary>
public class RadikoPlaylistClient(IHttpClientFactory httpClientFactory, IAppConfigurationService config)
{
    private static readonly TimeSpan LivePlaylistStartSyncTimeout = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan LivePlaylistStartSyncPollInterval = TimeSpan.FromSeconds(1);

    public Task<HttpResponseMessage> SendAsync(Uri targetUri, string token, CancellationToken cancellationToken) =>
        SendRadikoProxyRequestAsync(httpClientFactory.CreateClient(HttpClientNames.Radiko), config, targetUri, token, cancellationToken);

    public Task<(string? Playlist, Uri? PlaylistBaseUri, int StatusCode)> ResolveLiveAsync(
        ILogger logger, Uri targetUri, string token, DateTimeOffset? recordingStartUtc, CancellationToken cancellationToken) =>
        ResolveLivePlaylistAsync(logger, httpClientFactory.CreateClient(HttpClientNames.Radiko), config, targetUri, token, recordingStartUtc, cancellationToken);

    private static async Task<HttpResponseMessage> SendRadikoProxyRequestAsync(
        HttpClient client,
        IAppConfigurationService config,
        Uri targetUri,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, targetUri);
        request.Headers.TryAddWithoutValidation("X-Radiko-Authtoken", token);
        request.Headers.TryAddWithoutValidation("User-Agent", config.ExternalServiceUserAgent);
        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static async Task<(string? Playlist, Uri? PlaylistBaseUri, int StatusCode)> ResolveLivePlaylistAsync(
        ILogger logger,
        HttpClient client,
        IAppConfigurationService config,
        Uri targetUri,
        string token,
        DateTimeOffset? recordingStartUtc,
        CancellationToken cancellationToken)
    {
        using var upstreamResponse = await SendRadikoProxyRequestAsync(client, config, targetUri, token, cancellationToken);
        if (!upstreamResponse.IsSuccessStatusCode)
        {
            return (null, null, (int)upstreamResponse.StatusCode);
        }

        var upstreamContent = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!upstreamContent.Contains("#EXT-X-STREAM-INF", StringComparison.Ordinal))
        {
            logger.ZLogDebug($"radiko live proxy target is already media playlist. target={targetUri}");
            return (upstreamContent, targetUri, 200);
        }

        var mediaPlaylistUri = RadikoPlaylistProcessor.ExtractFirstPlaylistUri(upstreamContent, targetUri);
        if (mediaPlaylistUri == null)
        {
            return (null, null, 502);
        }

        logger.ZLogDebug($"radiko live proxy resolved media playlist. master={targetUri} media={mediaPlaylistUri}");

        string? mediaPlaylist = null;
        var syncAttempt = 0;
        var syncDeadlineUtc = recordingStartUtc.HasValue
            ? DateTimeOffset.UtcNow.Add(LivePlaylistStartSyncTimeout)
            : (DateTimeOffset?)null;

        while (true)
        {
            syncAttempt++;
            using var mediaPlaylistResponse = await SendRadikoProxyRequestAsync(client, config, mediaPlaylistUri, token, cancellationToken);
            if (!mediaPlaylistResponse.IsSuccessStatusCode)
            {
                return (null, null, (int)mediaPlaylistResponse.StatusCode);
            }

            mediaPlaylist = await mediaPlaylistResponse.Content.ReadAsStringAsync(cancellationToken);
            if (recordingStartUtc is null)
            {
                break;
            }

            var lastSegmentEndUtc = RadikoPlaylistProcessor.GetLastSegmentEndUtc(mediaPlaylist);
            if (lastSegmentEndUtc is { } lastEndUtc && lastEndUtc >= recordingStartUtc.Value)
            {
                logger.ZLogDebug(
                    $"radiko live playlist start sync reached target. targetStartUtc={recordingStartUtc:O} lastSegmentEndUtc={lastEndUtc:O} attempt={syncAttempt} media={mediaPlaylistUri}");
                break;
            }

            if (syncDeadlineUtc.HasValue && DateTimeOffset.UtcNow >= syncDeadlineUtc.Value)
            {
                logger.ZLogWarning(
                    $"radiko live playlist start sync timed out. targetStartUtc={recordingStartUtc:O} lastSegmentEndUtc={lastSegmentEndUtc:O} attempt={syncAttempt} media={mediaPlaylistUri}");
                break;
            }

            logger.ZLogDebug(
                $"radiko live playlist start sync waiting. targetStartUtc={recordingStartUtc:O} lastSegmentEndUtc={lastSegmentEndUtc:O} attempt={syncAttempt} media={mediaPlaylistUri}");
            await Task.Delay(LivePlaylistStartSyncPollInterval, cancellationToken);
        }

        mediaPlaylist = RadikoPlaylistProcessor.TrimLiveMediaPlaylistForRecording(
            mediaPlaylist ?? string.Empty,
            DateTimeOffset.UtcNow,
            logger,
            recordingStartUtc);
        return (mediaPlaylist, mediaPlaylistUri, 200);
    }
}
