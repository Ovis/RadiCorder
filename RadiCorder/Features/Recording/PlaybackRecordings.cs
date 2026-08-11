using Microsoft.AspNetCore.Mvc;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Services;

namespace RadiCorder.Features.Recording;

/// <summary>
/// 録音済み番組の再生・ダウンロード系エンドポイントを提供する。
/// </summary>
public static class PlaybackRecordings
{
    /// <summary>
    /// 録音済み番組の再生・ダウンロード系エンドポイントをマッピングする。
    /// </summary>
    public static RouteGroupBuilder MapRecordingPlaybackEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/download/{recordId}", HandleDownloadAsync)
            .WithName("ApiDownloadRecording")
            .WithSummary("録音ファイルをダウンロードする");
        group.MapGet("/play/{recordId}", HandlePlayAsync)
            .WithName("ApiPlayRecording")
            .WithSummary("録音ファイルを HLS で再生する");
        return group;
    }

    /// <summary>
    /// 録音ファイルをダウンロードする。
    /// </summary>
    private static async Task<IResult> HandleDownloadAsync(
        string recordId,
        [FromServices] IAppConfigurationService config,
        [FromServices] RecordedRadioLobLogic recordedRadioLobLogic)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return Results.BadRequest("recordId is required.");
        }

        if (!Ulid.TryParse(recordId, out var recordUlid))
        {
            return Results.BadRequest("Invalid record id.");
        }

        var (isSuccess, filePath) = await recordedRadioLobLogic.GetRecordedProgramFilePathAsync(recordUlid);
        if (!isSuccess)
        {
            return Results.NotFound("File not found.");
        }

        await recordedRadioLobLogic.MarkAsListenedAsync(recordUlid);

        if (!TryResolveManagedPath(filePath, out var fileFullPath, config.RecordFileSaveDir))
        {
            return Results.BadRequest("Invalid file path.");
        }

        if (!File.Exists(fileFullPath))
        {
            return Results.NotFound("File not found.");
        }

        var fileName = Path.GetFileName(fileFullPath);
        var fileStream = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read);
        return Results.File(fileStream, "application/octet-stream", fileName);
    }

    /// <summary>
    /// 録音ファイルを HLS で再生する。
    /// </summary>
    private static async Task<IResult> HandlePlayAsync(
        string recordId,
        [FromServices] IAppConfigurationService config,
        [FromServices] RecordedRadioLobLogic recordedRadioLobLogic)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return Results.BadRequest("recordId is required.");
        }

        if (!Ulid.TryParse(recordId, out var recordUlid))
        {
            return Results.BadRequest("Invalid record id.");
        }

        var (isSuccess, filePath) = await recordedRadioLobLogic.GetHlsAsync(recordUlid);
        if (!isSuccess)
        {
            return Results.NotFound("File not found.");
        }

        await recordedRadioLobLogic.MarkAsListenedAsync(recordUlid);

        var hlsRoot = TemporaryStoragePaths.GetHlsCacheRootDirectory(config.TemporaryFileSaveDir);
        if (!TryResolveManagedPath(filePath, out var fileFullPath, config.RecordFileSaveDir, hlsRoot))
        {
            return Results.BadRequest("Invalid file path.");
        }

        if (!File.Exists(fileFullPath))
        {
            return Results.NotFound("File not found.");
        }

        var m3u8Content = await File.ReadAllTextAsync(fileFullPath);
        return Results.Content(m3u8Content, "application/x-mpegURL");
    }

    /// <summary>
    /// 管理対象ルート配下のファイルパスかどうかを検証して絶対パスへ解決する。
    /// </summary>
    private static bool TryResolveManagedPath(string storedPath, out string fullPath, params string[] allowedBaseDirs)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(storedPath) || allowedBaseDirs.Length == 0)
        {
            return false;
        }

        var normalizedBases = allowedBaseDirs
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(x).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToList();

        if (normalizedBases.Count == 0)
        {
            return false;
        }

        if (Path.IsPathRooted(storedPath))
        {
            var candidate = Path.GetFullPath(storedPath);
            if (!IsUnderAnyBase(candidate, normalizedBases))
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }

        // 相対パスは許可されたベースディレクトリ配下に限定して結合する。
        foreach (var baseDir in normalizedBases)
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDir, storedPath));
            if (IsUnderAnyBase(candidate, normalizedBases))
            {
                fullPath = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 対象パスが許可ベースディレクトリ配下かどうかを判定する。
    /// </summary>
    private static bool IsUnderAnyBase(string candidatePath, IReadOnlyList<string> baseDirs)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (var baseDir in baseDirs)
        {
            var withSeparator = baseDir + Path.DirectorySeparatorChar;
            if (candidatePath.Equals(baseDir, comparison) || candidatePath.StartsWith(withSeparator, comparison))
            {
                return true;
            }
        }

        return false;
    }
}

