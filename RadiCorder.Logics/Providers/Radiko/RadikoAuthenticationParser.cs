using System.Text.RegularExpressions;

namespace RadiCorder.Logics.Providers.Radiko;

/// <summary>
/// 認証に必要な外部JavaScriptの形式を検証する。
/// </summary>
public static class RadikoAuthenticationParser
{
    public static bool TryGetPartialKey(string javascript, out string key)
    {
        var match = Regex.Match(javascript,
            """new\s+RadikoJSPlayer\s*\(\s*[^,]+,\s*[^,]+,\s*(['"])(?<key>[A-Za-z0-9]+)\1\s*,""",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        key = match.Success ? match.Groups["key"].Value : string.Empty;
        return key.Length > 0;
    }
}
