using System.Text;
using System.Text.RegularExpressions;

namespace RadiCorder.Logics.Extensions;

/// <summary>
/// タグ名正規化拡張
/// </summary>
public static partial class TagNormalizationExtensions
{
    /// <summary>
    /// タグ名を正規化する
    /// </summary>
    public static string NormalizeTagName(this string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        // 全角英数字を半角へ
        var normalized = source.Trim().Normalize(NormalizationForm.FormKC);
        // 記号揺れ
        normalized = normalized.Replace('・', '･');
        // 連続空白を1つに圧縮
        normalized = MultiSpaceRegex().Replace(normalized, " ");
        // 大文字小文字を統一
        normalized = normalized.ToLowerInvariant();

        return normalized;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
