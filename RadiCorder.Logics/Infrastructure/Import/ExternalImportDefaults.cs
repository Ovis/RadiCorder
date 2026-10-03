namespace RadiCorder.Logics.Infrastructure.Import;

/// <summary>
/// 外部取込で共通の既定値と入力制限。
/// </summary>
internal static class ExternalImportDefaults
{
    internal static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".m4a",
        ".aac",
        ".flac",
        ".wav",
        ".ogg",
        ".opus",
        ".wma"
    };

    internal const string DefaultStationName = "不明";
    internal const string DefaultTagName = "外部取込";
    internal static readonly string DefaultTagNormalizedName = DefaultTagName.ToLowerInvariant();
    internal const int CsvMaxRows = 5000;
}
