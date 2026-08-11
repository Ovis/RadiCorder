using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic;

/// <summary>
/// 録音済み番組のファイル操作・HLS生成を担当するサービス
/// </summary>
public class RecordedProgramMediaService(
    ILogger<RecordedProgramMediaService> logger,
    IAppConfigurationService config,
    IFfmpegService ffmpegService,
    RadioDbContext dbContext)
{
    private const string MediaTrackInfoFileName = "radio.m3u8";

    /// <summary>
    /// 録音済み番組を削除する
    /// </summary>
    /// <param name="recorderId">削除対象録音ID</param>
    /// <param name="deletePhysicalFiles">ファイルも削除する場合は <c>true</c></param>
    public async ValueTask<bool> DeleteRecordedProgramAsync(Ulid recorderId, bool deletePhysicalFiles = true)
    {
        if (deletePhysicalFiles && !await DeletePhysicalFilesAsync(recorderId))
        {
            return false;
        }

        try
        {
            var recording = await dbContext.Recordings.FindAsync(recorderId);
            if (recording == null)
            {
                return false;
            }

            // Recordingを削除すれば関連データもカスケード削除される
            dbContext.Recordings.Remove(recording);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"録音済み番組の削除に失敗しました。");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 録音番組の実ファイルと生成済みHLSを削除する
    /// </summary>
    private async ValueTask<bool> DeletePhysicalFilesAsync(Ulid recorderId)
    {
        // 録音番組ファイル削除
        {
            var (isSuccess, filePath) = await GetRecordedProgramFilePathAsync(recorderId);
            if (isSuccess && !string.IsNullOrEmpty(filePath))
            {
                if (!TryResolveFileFullPath(filePath, out var fileFullPath))
                {
                    logger.ZLogWarning($"録音ファイルパスの解決に失敗しました。recordingId={recorderId}");
                    return false;
                }

                if (File.Exists(fileFullPath))
                {
                    try
                    {
                        File.Delete(fileFullPath);
                    }
                    catch (Exception ex)
                    {
                        logger.ZLogError(ex, $"ファイルの削除に失敗しました。");
                        return false;
                    }
                }
            }
        }

        // HLSファイルの削除
        {
            var (isSuccess, path) = await GetHlsAsync(recorderId, false);
            if (isSuccess && !string.IsNullOrEmpty(path) && File.Exists(path))
            {
                // Pathのファイルのあるフォルダごと削除する
                var dir = Path.GetDirectoryName(path);
                try
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        Directory.Delete(dir, true);
                    }
                }
                catch (Exception ex)
                {
                    logger.ZLogError(ex, $"HLSファイルの削除に失敗しました。");
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 指定された録音済み番組のファイルパスを取得する
    /// </summary>
    public async ValueTask<(bool IsSuccess, string FilePath)> GetRecordedProgramFilePathAsync(Ulid recorderId)
    {
        try
        {
            var file = await dbContext.RecordingFiles.FindAsync(recorderId);
            if (file == null)
            {
                return (false, string.Empty);
            }

            // 相対パス優先、無ければフルパスを返す
            if (!string.IsNullOrEmpty(file.FileRelativePath))
                return (true, file.FileRelativePath);

            return (false, string.Empty);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"番組検索に失敗しました。", ex);
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// HLSファイルのパスを取得する
    /// HLSファイルが存在しない場合は生成する
    /// </summary>
    public async ValueTask<(bool IsSuccess, string Path)> GetHlsAsync(Ulid recorderId, bool createHls = true)
    {
        RecordingFile? recorderFile;
        try
        {
            recorderFile = await dbContext.RecordingFiles.FindAsync(recorderId);
            if (recorderFile == null)
            {
                return (false, string.Empty);
            }
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"番組検索に失敗しました。", ex);
            return (false, string.Empty);
        }

        if (recorderFile.HasHlsFile)
        {
            var hlsPath = HlsFilePath(recorderId);
            if (File.Exists(hlsPath))
            {
                if (!await IsInvalidHlsPlaylistAsync(hlsPath))
                {
                    return (true, hlsPath);
                }

                logger.ZLogWarning($"既存HLSプレイリストが無効なため再生成します。recordingId={recorderId}");
                CleanupGeneratedHlsArtifacts(Path.GetDirectoryName(hlsPath) ?? string.Empty);
                await TryResetHlsStateAsync(recorderFile);
            }
            else
            {
                // DB上はHLSありでも実体がない場合は補正して再生成にフォールバックする
                await TryResetHlsStateAsync(recorderFile);
            }
        }

        if (createHls)
        {
            var (isSuccess, path) = await GenerateHlsFileAsync(recorderId, recorderFile.FileRelativePath);
            if (!isSuccess)
            {
                return (false, string.Empty);
            }

            return (true, path);
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// HLS状態の不整合を補正する
    /// </summary>
    private async ValueTask TryResetHlsStateAsync(RecordingFile recorderFile)
    {
        recorderFile.HasHlsFile = false;
        recorderFile.HlsDirectoryPath = null;

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"HLS状態補正の保存に失敗しました。recordingId={recorderFile.RecordingId}");
        }
    }

    /// <summary>
    /// HLSファイルを生成する
    /// </summary>
    private async ValueTask<(bool IsSuccess, string Path)> GenerateHlsFileAsync(Ulid recorderId, string fileRelativePath)
    {
        if (!TryResolveFileFullPath(fileRelativePath, out var filePath))
        {
            logger.ZLogWarning($"HLS生成対象ファイルパスの解決に失敗しました。recordingId={recorderId}");
            return (false, string.Empty);
        }

        if (!File.Exists(filePath))
        {
            return (false, string.Empty);
        }

        var outputDir = HlsDirectoryPath(recorderId);
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var output = Path.Combine(outputDir, "radio%03d.ts");
        var mediaTrackInfoFileName = HlsFilePath(recorderId);

        var copyCommand =
            $"-i \"{filePath}\" -map 0:a:0 -vn -sn -dn -c:a copy -start_number 0 -hls_time 10 -hls_list_size 0 " +
            $"-hls_flags independent_segments -f hls -hls_segment_filename \"{output}\" \"{mediaTrackInfoFileName}\"";
        var ffmpegResult = await ffmpegService.RunProcessAsync(copyCommand, 300);
        if (!ffmpegResult)
        {
            logger.ZLogWarning($"HLS生成に失敗しました。recordingId={recorderId}");
            return (false, string.Empty);
        }

        if (!File.Exists(mediaTrackInfoFileName))
        {
            logger.ZLogWarning($"HLSプレイリストが生成されませんでした。recordingId={recorderId} path={mediaTrackInfoFileName}");
            return (false, string.Empty);
        }

        if (await IsInvalidHlsPlaylistAsync(mediaTrackInfoFileName))
        {
            logger.ZLogWarning($"HLSプレイリストが無効なため再生成します。recordingId={recorderId}");
            CleanupGeneratedHlsArtifacts(outputDir);

            var encodeCommand =
                $"-i \"{filePath}\" -map 0:a:0 -vn -sn -dn -c:a aac -b:a 128k -ar 48000 -ac 2 -start_number 0 -hls_time 10 -hls_list_size 0 " +
                $"-hls_flags independent_segments -f hls -hls_segment_filename \"{output}\" \"{mediaTrackInfoFileName}\"";
            var reEncodeResult = await ffmpegService.RunProcessAsync(encodeCommand, 300);
            if (!reEncodeResult || !File.Exists(mediaTrackInfoFileName) || await IsInvalidHlsPlaylistAsync(mediaTrackInfoFileName))
            {
                logger.ZLogWarning($"HLS再生成に失敗しました。recordingId={recorderId}");
                return (false, string.Empty);
            }
        }

        var mediaTrackInfoText = await File.ReadAllTextAsync(mediaTrackInfoFileName);
        mediaTrackInfoText = mediaTrackInfoText.Replace("radio", $"/static/{recorderId}/radio");
        await File.WriteAllTextAsync(mediaTrackInfoFileName, mediaTrackInfoText);

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            var recorderFile = await dbContext.RecordingFiles.FindAsync(recorderId);
            if (recorderFile == null)
            {
                return (false, string.Empty);
            }

            recorderFile.HasHlsFile = true;
            recorderFile.HlsDirectoryPath = outputDir;

            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            logger.ZLogError(e, $"HLS存在判定更新失敗", e);
            await transaction.RollbackAsync();
            // 作成はできているのでOKとする
        }

        return (true, mediaTrackInfoFileName);
    }

    private static void CleanupGeneratedHlsArtifacts(string outputDir)
    {
        if (!Directory.Exists(outputDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(outputDir, "radio*.*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // 次処理を継続
            }
        }
    }

    private static async ValueTask<bool> IsInvalidHlsPlaylistAsync(string playlistPath)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(playlistPath);
        }
        catch
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hasPositiveExtInf = lines
            .Where(line => line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["#EXTINF:".Length..].Split(',')[0])
            .Select(value => double.TryParse(value, out var duration) ? duration : 0d)
            .Any(duration => duration > 0d);

        var hasPositiveTargetDuration = lines
            .Where(line => line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["#EXT-X-TARGETDURATION:".Length..])
            .Select(value => int.TryParse(value, out var duration) ? duration : 0)
            .Any(duration => duration > 0);

        return !hasPositiveExtInf || !hasPositiveTargetDuration;
    }

    /// <summary>
    /// HLS格納ディレクトリ
    /// </summary>
    private string HlsDirectoryPath(Ulid recorderId)
    {
        return Path.Combine(
            TemporaryStoragePaths.GetHlsCacheRootDirectory(config.TemporaryFileSaveDir),
            recorderId.ToString());
    }

    /// <summary>
    /// HLSファイルパス
    /// </summary>
    private string HlsFilePath(Ulid recorderId)
    {
        return Path.Combine(HlsDirectoryPath(recorderId), MediaTrackInfoFileName);
    }

    /// <summary>
    /// 相対/絶対パスからフルパスを解決する
    /// </summary>
    private bool TryResolveFileFullPath(string path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var basePath = Path.GetFullPath(config.RecordFileSaveDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string candidatePath;
        if (Path.IsPathRooted(path))
        {
            candidatePath = Path.GetFullPath(path);
        }
        else if (config.RecordFileSaveDir.TryCombinePaths(path, out var combined))
        {
            candidatePath = combined;
        }
        else
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var baseWithSeparator = basePath + Path.DirectorySeparatorChar;
        var isUnderBase =
            candidatePath.Equals(basePath, comparison) ||
            candidatePath.StartsWith(baseWithSeparator, comparison);
        if (!isUnderBase)
        {
            return false;
        }

        fullPath = candidatePath;
        return true;
    }
}
