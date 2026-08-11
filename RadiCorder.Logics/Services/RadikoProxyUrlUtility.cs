namespace RadiCorder.Logics.Services;

/// <summary>
/// radiko proxy URL 生成ユーティリティ
/// </summary>
public static class RadikoProxyUrlUtility
{
    /// <summary>
    /// 相対プロキシURLを生成する
    /// </summary>
    public static string BuildRelativeProxyUrlWithProxyKey(
        string targetUrl,
        string proxyKey,
        bool resolveLivePlaylist = false,
        DateTimeOffset? recordingStartUtc = null)
    {
        var proxyPath = BuildProxyPath(targetUrl);
        var query = $"target={Uri.EscapeDataString(targetUrl)}&proxyKey={Uri.EscapeDataString(proxyKey)}";
        if (resolveLivePlaylist)
        {
            query += "&resolveLivePlaylist=true";
        }

        if (recordingStartUtc.HasValue)
        {
            query += $"&recordingStartUtc={Uri.EscapeDataString(recordingStartUtc.Value.ToString("O"))}";
        }

        return $"{proxyPath}?{query}";
    }

    /// <summary>
    /// 絶対プロキシURLを生成する
    /// </summary>
    public static string BuildAbsoluteProxyUrlWithProxyKey(
        string baseUrl,
        string targetUrl,
        string proxyKey,
        bool resolveLivePlaylist = false,
        DateTimeOffset? recordingStartUtc = null)
    {
        var relativeUrl = BuildRelativeProxyUrlWithProxyKey(targetUrl, proxyKey, resolveLivePlaylist, recordingStartUtc);
        return new Uri(new Uri(AppendTrailingSlash(baseUrl)), relativeUrl).ToString();
    }

    private static string AppendTrailingSlash(string url)
    {
        return url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";
    }

    private static string BuildProxyPath(string targetUrl)
    {
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var targetUri))
        {
            return "/api/programs/radiko-proxy";
        }

        var fileName = Path.GetFileName(targetUri.AbsolutePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "/api/programs/radiko-proxy";
        }

        return $"/api/programs/radiko-proxy/{Uri.EscapeDataString(fileName)}";
    }
}
