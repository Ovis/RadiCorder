namespace RadiCorder.Logics.Infrastructure.Import;

/// <summary>
/// 外部音声のmetadataと録音時間を読み取る。
/// </summary>
public static class ExternalImportAudioMetadata
{
    public static string ReadTitleMetadata(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            return tagFile.Tag.Title ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static TimeSpan ResolveRecordingDuration(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var duration = tagFile.Properties.Duration;
            if (duration > TimeSpan.Zero)
            {
                var roundedSeconds = Math.Max(1, Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
                return TimeSpan.FromSeconds(roundedSeconds);
            }
        }
        catch
        {
            // 読み取り不能なファイルは最小値へフォールバック
        }

        return TimeSpan.FromSeconds(1);
    }
}
