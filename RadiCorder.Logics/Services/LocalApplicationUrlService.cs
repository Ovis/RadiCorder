namespace RadiCorder.Logics.Services;

/// <summary>
/// 現在起動中アプリのローカルアクセス用URLを管理する
/// </summary>
public class LocalApplicationUrlService : ILocalApplicationUrlService
{
    private readonly object _sync = new();
    private string? _baseUrl;

    /// <summary>
    /// アプリ自身へアクセス可能なベースURLを返す
    /// </summary>
    public string? GetBaseUrl()
    {
        lock (_sync)
        {
            return _baseUrl;
        }
    }

    /// <summary>
    /// 起動中アプリのバインドURL候補を更新する
    /// </summary>
    public void SetCandidateUrls(IEnumerable<string> urls)
    {
        var resolved = SelectBaseUrl(urls);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        lock (_sync)
        {
            _baseUrl = resolved;
        }
    }

    private static string? SelectBaseUrl(IEnumerable<string> urls)
    {
        var candidates = urls
            .Select(NormalizeLoopbackUrl)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        return candidates
            .OrderBy(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(url => url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
    }

    private static string? NormalizeLoopbackUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var builder = new UriBuilder(uri);
        if (builder.Host is "0.0.0.0" or "[::]" or "::" or "*")
        {
            builder.Host = "127.0.0.1";
        }

        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            builder.Host = "127.0.0.1";
        }

        builder.Path = string.Empty;
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;
        return builder.Uri.GetLeftPart(UriPartial.Authority);
    }
}
