using RadiCorder.Logics.Application;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Services.Streaming;
using ZLogger;
using ProgramEndpointsMarker = RadiCorder.Features.Program.ProgramEndpoints.ProgramEndpointsMarker;

namespace RadiCorder.Features.Program;

/// <summary>
/// radiko proxy のHTTP入力と応答を扱う。
/// </summary>
internal static class RadikoStreamingEndpoints
{
    internal static RouteGroupBuilder MapRadikoStreamingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/radiko-proxy", HandleRadikoProxyAsync)
            .WithName("ApiProgramRadikoProxy")
            .WithSummary("radiko HLS を同一オリジン経由で中継する");
        group.MapGet("/radiko-proxy/{*hint}", HandleRadikoProxyAsync)
            .WithName("ApiProgramRadikoProxyWithHint")
            .WithSummary("radiko HLS を同一オリジン経由で中継する");
        return group;
    }

    /// <summary>
    /// radiko の HLS を同一オリジン経由で配信する。
    /// </summary>
    private static async Task<IResult> HandleRadikoProxyAsync(
        ILogger<ProgramEndpointsMarker> logger,
        RadikoPlaylistClient playlistClient,
        IRadikoProxyTicketService radikoProxyTicketService,
        string target,
        string? token,
        string? proxyKey,
        bool? resolveLivePlaylist,
        DateTimeOffset? recordingStartUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target) || (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(proxyKey)))
        {
            return Results.BadRequest("target and proxyKey/token are required.");
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var targetUri) || !RadikoPlaylistProcessor.IsAllowedRadikoProxyTarget(targetUri))
        {
            return Results.BadRequest("Invalid proxy target.");
        }

        var resolvedToken = ResolveProxyToken(radikoProxyTicketService, token, proxyKey);
        if (string.IsNullOrWhiteSpace(resolvedToken))
        {
            return Results.BadRequest("Invalid proxy credential.");
        }

        var effectiveProxyKey = !string.IsNullOrWhiteSpace(proxyKey)
            ? proxyKey!
            : radikoProxyTicketService.IssueTokenTicket(resolvedToken);

        try
        {
            if (resolveLivePlaylist == true)
            {
                var (resolvedPlaylist, playlistBaseUri, statusCode) = await playlistClient.ResolveLiveAsync(
                    logger,
                    targetUri,
                    resolvedToken,
                    recordingStartUtc,
                    cancellationToken);
                if (resolvedPlaylist == null || playlistBaseUri == null)
                {
                    logger.ZLogWarning($"radiko live proxy upstream failed. status={statusCode} target={targetUri}");
                    return Results.StatusCode(statusCode);
                }

                var rewritten = RadikoPlaylistProcessor.RewritePlaylistToLocalProxy(resolvedPlaylist, playlistBaseUri, effectiveProxyKey);
                return Results.Content(rewritten, "application/vnd.apple.mpegurl");
            }

            var response = await playlistClient.SendAsync(targetUri, resolvedToken, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                logger.ZLogWarning($"radiko proxy upstream failed. status={(int)response.StatusCode} target={targetUri}");
                return Results.StatusCode((int)response.StatusCode);
            }

            if (RadikoPlaylistProcessor.IsPlaylistRequest(targetUri, response.Content.Headers.ContentType?.MediaType))
            {
                using (response)
                {
                    var playlist = await HttpResponseBodyReader.ReadStringAsync(response.Content, HttpResponseBodyReader.PlaylistLimit, cancellationToken);
                    var rewritten = RadikoPlaylistProcessor.RewritePlaylistToLocalProxy(playlist, response.RequestMessage?.RequestUri ?? targetUri, effectiveProxyKey);
                    return Results.Content(rewritten, "application/vnd.apple.mpegurl");
                }
            }

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            return Results.Stream(
                async outputStream =>
                {
                    using (response)
                    {
                        using var bodyDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        bodyDeadline.CancelAfter(TimeSpan.FromSeconds(30));
                        await using var upstreamStream = await response.Content.ReadAsStreamAsync(bodyDeadline.Token);
                        await upstreamStream.CopyToAsync(outputStream, bodyDeadline.Token);
                    }
                },
                contentType);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"radiko proxy failed. target={targetUri}");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    private static string? ResolveProxyToken(
        IRadikoProxyTicketService radikoProxyTicketService,
        string? token,
        string? proxyKey)
    {
        if (!string.IsNullOrWhiteSpace(proxyKey))
        {
            return radikoProxyTicketService.TryGetToken(proxyKey, out var resolvedToken)
                ? resolvedToken
                : null;
        }

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
