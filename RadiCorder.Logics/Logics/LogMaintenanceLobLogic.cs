using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace RadiCorder.Logics.Logics;

/// <summary>
/// ログファイルのメンテナンス処理を提供する。
/// </summary>
public class LogMaintenanceLobLogic(
    ILogger<LogMaintenanceLobLogic> logger,
    IConfiguration configuration)
{
    /// <summary>
    /// 保持日数を超過したログファイルを削除する。
    /// </summary>
    /// <param name="retentionDays">保持日数</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public ValueTask CleanupOldLogFilesAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var normalizedRetentionDays = retentionDays > 0 ? retentionDays : 100;
        var configuredLogDirectory = configuration["RadiCorder:LogDirectory"];
        var logDirectory = string.IsNullOrWhiteSpace(configuredLogDirectory)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs")
            : (Path.IsPathRooted(configuredLogDirectory)
                ? configuredLogDirectory
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredLogDirectory));
        if (!Directory.Exists(logDirectory))
        {
            return ValueTask.CompletedTask;
        }

        var threshold = DateTimeOffset.UtcNow.AddDays(-normalizedRetentionDays);
        var deletedCount = 0;

        foreach (var filePath in Directory.EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var lastWriteTime = File.GetLastWriteTimeUtc(filePath);
                if (lastWriteTime >= threshold.UtcDateTime)
                {
                    continue;
                }

                File.Delete(filePath);
                deletedCount++;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, $"古いログファイルの削除に失敗しました。path={filePath}");
            }
        }

        logger.ZLogInformation($"ログメンテナンス完了。削除件数={deletedCount} 保持日数={normalizedRetentionDays}");
        return ValueTask.CompletedTask;
    }
}
