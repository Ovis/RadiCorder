using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics;

/// <summary>
/// 一時保存領域のガベージを用途別にクリーンアップする。
/// </summary>
public class TemporaryStorageMaintenanceLobLogic(
    ILogger<TemporaryStorageMaintenanceLobLogic> logger,
    IAppConfigurationService config,
    RadioDbContext dbContext)
{
    private const int WorkFileRetentionDays = 30;
    private const int HlsCacheRetentionDays = 60;

    /// <summary>
    /// 一時保存領域のクリーンアップを実行する。
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async ValueTask CleanupAsync(CancellationToken cancellationToken = default)
    {
        var temporaryRoot = config.TemporaryFileSaveDir;
        if (string.IsNullOrWhiteSpace(temporaryRoot) || !Directory.Exists(temporaryRoot))
        {
            return;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var workCutoffUtc = nowUtc.AddDays(-WorkFileRetentionDays).UtcDateTime;
        var hlsCutoffUtc = nowUtc.AddDays(-HlsCacheRetentionDays).UtcDateTime;

        var recordingsWorkDir = TemporaryStoragePaths.GetRecordingsWorkDirectory(temporaryRoot);
        var timeFreeWorkDir = TemporaryStoragePaths.GetTimeFreeWorkDirectory(temporaryRoot);
        var hlsCacheRootDir = TemporaryStoragePaths.GetHlsCacheRootDirectory(temporaryRoot);

        var removedRecordingsWorkFiles = CleanupOldFiles(recordingsWorkDir, workCutoffUtc, cancellationToken);
        var removedTimeFreeWorkFiles = CleanupOldFiles(timeFreeWorkDir, workCutoffUtc, cancellationToken);
        var (removedHlsDirs, resetHlsFlags) = await CleanupOldHlsCacheAsync(hlsCacheRootDir, hlsCutoffUtc, cancellationToken);

        logger.ZLogInformation(
            $"一時保存領域クリーンアップ完了。recordings-work={removedRecordingsWorkFiles}, timefree-work={removedTimeFreeWorkFiles}, hls-cache={removedHlsDirs}, hlsFlagsReset={resetHlsFlags}");
    }

    /// <summary>
    /// 指定ディレクトリ配下の古いファイルを削除する。
    /// </summary>
    private int CleanupOldFiles(string rootDir, DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootDir))
        {
            return 0;
        }

        var deletedCount = 0;
        foreach (var filePath in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var lastWriteUtc = File.GetLastWriteTimeUtc(filePath);
                if (lastWriteUtc >= cutoffUtc)
                {
                    continue;
                }

                File.Delete(filePath);
                deletedCount++;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, $"古い一時ファイルの削除に失敗しました。path={filePath}");
            }
        }

        // 空ディレクトリを削除しておく
        foreach (var dirPath in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
                {
                    Directory.Delete(dirPath, false);
                }
            }
            catch
            {
                // 後続処理を優先して握る
            }
        }

        return deletedCount;
    }

    /// <summary>
    /// 古いHLSキャッシュを削除し、DB上のHLS状態を整合させる。
    /// </summary>
    private async ValueTask<(int RemovedDirs, int ResetFlags)> CleanupOldHlsCacheAsync(
        string hlsCacheRootDir,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var removedDirs = 0;
        var resetFlags = 0;

        var filesWithHls = await dbContext.RecordingFiles
            .Where(r => r.HasHlsFile)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var file in filesWithHls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hlsDir = file.HlsDirectoryPath;
            if (string.IsNullOrWhiteSpace(hlsDir))
            {
                file.HasHlsFile = false;
                file.HlsDirectoryPath = null;
                resetFlags++;
                changed = true;
                continue;
            }

            if (!Directory.Exists(hlsDir))
            {
                file.HasHlsFile = false;
                file.HlsDirectoryPath = null;
                resetFlags++;
                changed = true;
                continue;
            }

            var lastWriteUtc = Directory.GetLastWriteTimeUtc(hlsDir);
            if (lastWriteUtc >= cutoffUtc)
            {
                continue;
            }

            try
            {
                Directory.Delete(hlsDir, true);
                removedDirs++;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, $"古いHLSキャッシュの削除に失敗しました。path={hlsDir}");
                continue;
            }

            file.HasHlsFile = false;
            file.HlsDirectoryPath = null;
            resetFlags++;
            changed = true;
        }

        // DB管理外の孤立HLSディレクトリも削除
        if (Directory.Exists(hlsCacheRootDir))
        {
            var managedPaths = filesWithHls
                .Select(r => r.HlsDirectoryPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFullPath(p!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in Directory.EnumerateDirectories(hlsCacheRootDir, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullDir = Path.GetFullPath(dir);
                if (managedPaths.Contains(fullDir))
                {
                    continue;
                }

                var lastWriteUtc = Directory.GetLastWriteTimeUtc(fullDir);
                if (lastWriteUtc >= cutoffUtc)
                {
                    continue;
                }

                try
                {
                    Directory.Delete(fullDir, true);
                    removedDirs++;
                }
                catch (Exception ex)
                {
                    logger.ZLogWarning(ex, $"孤立HLSキャッシュの削除に失敗しました。path={fullDir}");
                }
            }
        }

        if (changed)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (removedDirs, resetFlags);
    }
}

