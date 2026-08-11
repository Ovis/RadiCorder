namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// 一時保存領域の用途別サブフォルダ名とパス解決を提供する。
/// </summary>
public static class TemporaryStoragePaths
{
    public const string RecordingsWorkDirectoryName = "recordings-work";
    public const string SaveFailedDirectoryName = "save-failed";
    public const string HlsCacheDirectoryName = "hls-cache";
    public const string TimeFreeWorkDirectoryName = "timefree-work";
    public const string LogoImageDirectoryName = "logo-image";

    public static string GetRecordingsWorkDirectory(string temporaryRoot)
    {
        return Path.Combine(temporaryRoot, RecordingsWorkDirectoryName);
    }

    public static string GetHlsCacheRootDirectory(string temporaryRoot)
    {
        return Path.Combine(temporaryRoot, HlsCacheDirectoryName);
    }

    public static string GetSaveFailedDirectory(string temporaryRoot)
    {
        return Path.Combine(temporaryRoot, SaveFailedDirectoryName);
    }

    public static string GetTimeFreeWorkDirectory(string temporaryRoot)
    {
        return Path.Combine(temporaryRoot, TimeFreeWorkDirectoryName);
    }

    public static string GetLogoImageDirectory(string temporaryRoot)
    {
        return Path.Combine(temporaryRoot, LogoImageDirectoryName);
    }
}
