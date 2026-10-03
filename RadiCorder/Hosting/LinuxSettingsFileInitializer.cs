using System.Text;

namespace RadiCorder.Hosting;

/// <summary>
/// Linux の初回設定ファイルを準備する。
/// </summary>
public static class LinuxSettingsFileInitializer
{
    public static string? EnsureLinuxSettingsFile(string localSettingsPath, string systemSettingsPath, string contentRootPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        if (File.Exists(localSettingsPath) || File.Exists(systemSettingsPath))
        {
            return null;
        }

        try
        {
            var samplePath = Path.Combine(contentRootPath, "radicorder.settings.sample.json");

            var payload = File.Exists(samplePath) ?
                File.ReadAllText(samplePath, Encoding.UTF8)
                : """
                  {
                    "RadiCorder": {
                      "RecordFileSaveFolder": "record",
                      "TemporaryFileSaveFolder": "temp"
                    }
                  }
                  """;

            File.WriteAllText(localSettingsPath, payload, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return localSettingsPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RadiCorder] Linux設定ファイルの自動生成に失敗しました。 path={localSettingsPath} error={ex.Message}");
            return null;
        }
    }
}
