using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// 録音実行（FFmpeg）を行う実装
/// </summary>
public class MediaTranscodeService(
    ILogger<MediaTranscodeService> logger,
    IFfmpegService ffmpegService,
    IAppConfigurationService config,
    IHttpClientFactory? httpClientFactory = null) : IMediaTranscodeService
{
    private const int TimeFreeChunkSecondsMax = 300;
    private const int TimeFreeChunkUnitSeconds = 5;
    private const int NonRealtimeRetryMaxAttempts = 3; // 初回 + リトライ2回
    private const int NonRealtimeRetryInitialDelaySeconds = 30;
    private const int RadikoRealTimeTailCompensationSeconds = 10;
    private static readonly Encoding FileListEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 録音を実行する
    /// </summary>
    public async ValueTask<bool> RecordAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken = default)
    {
        // タイムフリー録音はradikoのみ対応
        if (source.Options.IsTimeFree && source.Options.ServiceKind != RadioServiceKind.Radiko)
        {
            logger.ZLogWarning($"タイムフリー録音はradikoのみ対応です。");
            return false;
        }

        var recorded = source.Options.IsOnDemand
            ? await RecordOnDemandAsync(source, path, cancellationToken)
            : source.Options.IsTimeFree
                ? await RecordTimeFreeAsync(source, path, cancellationToken)
                : await RecordRealTimeAsync(source, path, cancellationToken);

        if (!recorded)
        {
            return false;
        }

        await TryAttachProgramImageAsCoverArtAsync(source.ProgramInfo, path, cancellationToken);
        return true;
    }

    /// <summary>
    /// 聞き逃し配信録音
    /// </summary>
    private async ValueTask<bool> RecordOnDemandAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.StreamUrl))
        {
            logger.ZLogError($"聞き逃し配信録音URLが空です。");
            return false;
        }

        var duration = source.ProgramInfo.EndTime - source.ProgramInfo.StartTime;
        var timeout = (int)Math.Clamp(duration.TotalSeconds + 600, 600, 7200);

        var command = new StringBuilder();
        command.Append(" -nostdin -loglevel error -stats");
        AppendUserAgent(command, config.ExternalServiceUserAgent);
        AppendHeaders(command, source.Headers);
        command.Append(" -http_seekable 0 -seekable 0");
        command.Append($" -i \"{source.StreamUrl}\"");
        command.Append(" -acodec copy -vn -bsf:a aac_adtstoasc");
        AppendProgramInfo(command, source.ProgramInfo);
        command.Append($" -y \"{path.TempFilePath}\"");

        logger.ZLogDebug($"聞き逃し配信録音開始: station={source.ProgramInfo.StationId} title={source.ProgramInfo.Title} programId={source.ProgramInfo.ProgramId}");

        return await RunFfmpegWithRetryAsync(
            operationName: "聞き逃し配信録音",
            ffmpegArguments: command.ToString(),
            timeoutSeconds: timeout,
            loggingProgramName: $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}_ondemand",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// タイムフリー録音
    /// </summary>
    private async ValueTask<bool> RecordTimeFreeAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.StreamUrl))
        {
            logger.ZLogError($"タイムフリー録音URLが空です。");
            return false;
        }

        var startTime = source.ProgramInfo.StartTime;
        var endTime = source.ProgramInfo.EndTime;

        if (startTime >= endTime)
        {
            logger.ZLogError($"タイムフリー録音の開始/終了時刻が不正です。");
            return false;
        }

        var tmpDir = TemporaryStoragePaths.GetTimeFreeWorkDirectory(config.TemporaryFileSaveDir);
        var baseName = $"radiko_ts_{Guid.NewGuid():N}";
        var fileListPath = Path.Combine(tmpDir, $"{baseName}_filelist.txt");

        Directory.CreateDirectory(tmpDir);

        // concat用のファイルリストはBOMなしで生成する
        await File.WriteAllTextAsync(fileListPath, string.Empty, FileListEncoding);

        var stationId = source.RequestStationIdOverride ?? source.ProgramInfo.StationId;
        var startAt = ToRadikoTimeString(startTime);
        var lsid = Guid.NewGuid().ToString("N");

        var ok = true;
        var seekTime = startTime;
        var leftSeconds = (int)Math.Floor((endTime - startTime).TotalSeconds);
        var chunkNo = 0;

        try
        {
            while (leftSeconds > 0)
            {
                var chunkSeconds = GetTimeFreeChunkSeconds(leftSeconds);
                var seek = ToRadikoTimeString(seekTime);
                var endAtTime = seekTime.AddSeconds(chunkSeconds);
                var endAt = ToRadikoTimeString(endAtTime);

                var url = BuildTimeFreeChunkUrl(
                    baseUrl: source.StreamUrl,
                    stationId: stationId,
                    startAt: startAt,
                    seek: seek,
                    endAt: endAt,
                    lengthSeconds: chunkSeconds,
                    lsid: lsid);

                var chunkFile = Path.Combine(tmpDir, $"{baseName}_chunk{chunkNo}.m4a");

                var command = new StringBuilder();
                command.Append(" -nostdin -loglevel error -stats");
                command.Append(" -fflags +discardcorrupt");
                AppendUserAgent(command, config.ExternalServiceUserAgent);
                AppendHeaders(command, source.Headers);
                command.Append(" -http_seekable 0 -seekable 0");
                command.Append($" -i \"{url}\"");
                command.Append(" -acodec copy -vn -bsf:a aac_adtstoasc -y");
                command.Append($" \"{chunkFile}\"");

                logger.ZLogDebug($"タイムフリー録音チャンク開始: chunk={chunkNo} seek={seek} end_at={endAt} l={chunkSeconds}s");

                var timeoutSeconds = Math.Clamp(chunkSeconds + 120, 120, 3600);
                if (!await RunFfmpegWithRetryAsync(
                    operationName: $"タイムフリー録音チャンク取得(chunk={chunkNo})",
                    ffmpegArguments: command.ToString(),
                    timeoutSeconds: timeoutSeconds,
                    loggingProgramName: $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}_chunk{chunkNo}",
                    cancellationToken: cancellationToken))
                {
                    ok = false;
                    break;
                }

                var chunkForList = Path.GetFullPath(chunkFile).Replace('\\', '/');
                await File.AppendAllTextAsync(fileListPath, $"file '{chunkForList}'\n", FileListEncoding);

                seekTime = seekTime.AddSeconds(chunkSeconds);
                leftSeconds -= chunkSeconds;
                chunkNo++;
            }

            if (!ok)
            {
                logger.ZLogError($"タイムフリー録音チャンク取得に失敗しました。");
                return false;
            }

            var concatCommand = new StringBuilder();
            concatCommand.Append(" -loglevel error -f concat -safe 0");
            concatCommand.Append($" -i \"{fileListPath}\"");
            concatCommand.Append(" -c copy");
            AppendProgramInfo(concatCommand, source.ProgramInfo);
            concatCommand.Append($" -y \"{path.TempFilePath}\"");

            logger.ZLogDebug($"タイムフリー録音結合開始: station={source.ProgramInfo.StationId} title={source.ProgramInfo.Title} start={source.ProgramInfo.StartTime:O} end={source.ProgramInfo.EndTime:O}");
            return await RunFfmpegWithRetryAsync(
                operationName: "タイムフリー録音結合",
                ffmpegArguments: concatCommand.ToString(),
                timeoutSeconds: 600,
                loggingProgramName: $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}_concat",
                cancellationToken: cancellationToken);
        }
        finally
        {
            CleanupTempFiles(tmpDir, baseName);
        }
    }

    /// <summary>
    /// リアルタイム録音
    /// </summary>
    private async ValueTask<bool> RecordRealTimeAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken)
    {
        var startTime = DateTimeOffset.UtcNow;
        if (source.ProgramInfo.StartTime >= startTime)
        {
            startTime = source.ProgramInfo.StartTime;
        }

        var tailCompensationSeconds = GetRealTimeTailCompensationSeconds(source);
        var diff = source.ProgramInfo.EndTime
            .AddSeconds(source.Options.StartDelaySeconds)
            .AddSeconds(source.Options.EndDelaySeconds)
            .AddSeconds(tailCompensationSeconds)
            - startTime;

        var command = new StringBuilder();
        // HLS ライブ入力は元から実時間で供給されるため、-re で入力を絞ると
        // ライブ窓から取りこぼしやすくなる。
        command.Append(" -vn -nostdin");
        AppendUserAgent(command, config.ExternalServiceUserAgent);
        AppendHeaders(command, source.Headers);
        command.Append(" -http_seekable 0 -seekable 0");
        command.Append(" -reconnect 1 -reconnect_streamed 1 -reconnect_on_network_error 1 -reconnect_delay_max 120");
        command.Append($" -i \"{source.StreamUrl}\"");
        command.Append($" -t {diff.TotalSeconds}");

        if (source.Options.ServiceKind == RadioServiceKind.Radiru)
        {
            // らじる★らじる向けの最適化
            command.Append(" -movflags +faststart");
        }

        command.Append(" -acodec copy -vn -bsf:a aac_adtstoasc -y");
        AppendProgramInfo(command, source.ProgramInfo);
        command.Append($" -y \"{path.TempFilePath}\"");

        var timeout = (int)diff.Add(new TimeSpan(0, 10, 0)).TotalSeconds;
        logger.ZLogDebug($"リアルタイム録音開始: station={source.ProgramInfo.StationId} title={source.ProgramInfo.Title} start={source.ProgramInfo.StartTime:O} end={source.ProgramInfo.EndTime:O} tailCompSec={tailCompensationSeconds} timeoutSec={timeout}");

        return await ffmpegService.RunProcessAsync(
            command.ToString(),
            timeout,
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}",
            cancellationToken);
    }

    private static int GetRealTimeTailCompensationSeconds(RecordingSourceResult source)
    {
        return source.Options.ServiceKind == RadioServiceKind.Radiko ? RadikoRealTimeTailCompensationSeconds : 0;
    }

    /// <summary>
    /// HTTPヘッダーをFFmpegコマンドに追加する
    /// </summary>
    private static void AppendHeaders(StringBuilder command, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
            return;

        var headerValue = string.Join("\r\n", headers.Select(h => $"{h.Key}: {h.Value}")) + "\r\n";
        command.Append($" -headers \"{headerValue}\"");
    }

    /// <summary>
    /// HTTP User-Agent をFFmpegコマンドに追加する
    /// </summary>
    private static void AppendUserAgent(StringBuilder command, string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return;
        }

        command.Append($" -user_agent \"{userAgent.ToSafeNameAndSafeCommandParameter()}\"");
    }

    /// <summary>
    /// 番組情報をFFmpegコマンドに追加する
    /// </summary>
    private static void AppendProgramInfo(StringBuilder command, ProgramRecordingInfo programInfo)
    {
        command.Append($" -metadata title=\"{programInfo.Title.ToSafeNameAndSafeCommandParameter()}\"");
        command.Append($" -metadata comment=\"{programInfo.Description.ExtractTextFromHtml().ToSafeNameAndSafeCommandParameter()}\"");
        command.Append($" -metadata artist=\"{programInfo.Performer.ToSafeNameAndSafeCommandParameter()}\"");
        command.Append($" -metadata date=\"{programInfo.StartTime.ToJapanDateTime()}\"");
    }

    /// <summary>
    /// タイムフリー録音のチャンクURLを生成する
    /// </summary>
    private static string BuildTimeFreeChunkUrl(
        string baseUrl,
        string stationId,
        string startAt,
        string seek,
        string endAt,
        int lengthSeconds,
        string lsid)
    {
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}station_id={Uri.EscapeDataString(stationId)}" +
               $"&start_at={startAt}&ft={startAt}" +
               $"&seek={seek}&end_at={endAt}&to={endAt}" +
               $"&l={lengthSeconds}&lsid={lsid}&type=c";
    }

    /// <summary>
    /// タイムフリー録音のチャンク秒数を計算する
    /// </summary>
    private static int GetTimeFreeChunkSeconds(int leftSeconds)
    {
        if (leftSeconds <= 0) return 0;
        if (leftSeconds >= TimeFreeChunkSecondsMax) return TimeFreeChunkSecondsMax;

        return leftSeconds % TimeFreeChunkUnitSeconds == 0
            ? leftSeconds
            : ((leftSeconds / TimeFreeChunkUnitSeconds) + 1) * TimeFreeChunkUnitSeconds;
    }

    /// <summary>
    /// radiko向け日時フォーマット(yyyyMMddHHmmss)へ変換する
    /// </summary>
    private static string ToRadikoTimeString(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToJapanDateTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 一時ファイルを削除する
    /// </summary>
    private static void CleanupTempFiles(string tmpDir, string baseName)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(tmpDir, $"{baseName}_*"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 失敗しても次に進む
                }
            }
        }
        catch
        {
            // 失敗しても次に進む
        }
    }

    private async ValueTask<bool> RunFfmpegWithRetryAsync(
        string operationName,
        string ffmpegArguments,
        int timeoutSeconds,
        string loggingProgramName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= NonRealtimeRetryMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var success = await ffmpegService.RunProcessAsync(
                ffmpegArguments,
                timeoutSeconds,
                loggingProgramName,
                cancellationToken);
            if (success)
            {
                return true;
            }

            if (attempt >= NonRealtimeRetryMaxAttempts)
            {
                logger.ZLogError($"{operationName} が失敗しました。リトライ上限に到達しました。 attempts={NonRealtimeRetryMaxAttempts}");
                return false;
            }

            var delaySeconds = NonRealtimeRetryInitialDelaySeconds * (int)Math.Pow(2, attempt - 1);
            logger.ZLogWarning($"{operationName} が失敗したためリトライします。 attempt={attempt}/{NonRealtimeRetryMaxAttempts} nextDelaySec={delaySeconds}");
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }

        return false;
    }

    private async ValueTask TryAttachProgramImageAsCoverArtAsync(
        ProgramRecordingInfo programInfo,
        MediaPath path,
        CancellationToken cancellationToken)
    {
        if (!config.EmbedProgramImageOnRecord || string.IsNullOrWhiteSpace(programInfo.ImageUrl))
        {
            return;
        }

        var imagePath = string.Empty;
        var outputPath = string.Empty;

        try
        {
            var logoImageDirectory = TemporaryStoragePaths.GetLogoImageDirectory(config.TemporaryFileSaveDir);
            Directory.CreateDirectory(logoImageDirectory);

            var (imageBytes, extension) = await DownloadProgramImageAsync(programInfo.ImageUrl, cancellationToken);
            if (imageBytes.Length == 0)
            {
                return;
            }

            var uniqueName = $"{Ulid.NewUlid()}{extension}";
            imagePath = Path.Combine(logoImageDirectory, uniqueName);
            await File.WriteAllBytesAsync(imagePath, imageBytes, cancellationToken);

            outputPath = Path.Combine(logoImageDirectory, $"{Ulid.NewUlid()}.m4a");

            var command = new StringBuilder();
            command.Append(" -nostdin -loglevel error -stats");
            command.Append($" -i \"{path.TempFilePath}\"");
            command.Append($" -i \"{imagePath}\"");
            command.Append(" -map 0:a -map 1:v");
            command.Append(" -c:a copy -c:v mjpeg -disposition:v:0 attached_pic -movflags +faststart");
            command.Append($" -y \"{outputPath}\"");

            var attached = await ffmpegService.RunProcessAsync(
                command.ToString(),
                timeoutSeconds: 180,
                loggingProgramName: $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{programInfo.Title}_cover-art",
                cancellationToken);

            if (!attached || !File.Exists(outputPath))
            {
                logger.ZLogWarning($"番組イメージの埋め込みに失敗したためスキップします。 programId={programInfo.ProgramId}");
                return;
            }

            File.Copy(outputPath, path.TempFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"番組イメージのダウンロードまたは埋め込みに失敗したためスキップします。 programId={programInfo.ProgramId} imageUrl={programInfo.ImageUrl}");
        }
        finally
        {
            TryDeleteFile(imagePath);
            TryDeleteFile(outputPath);
        }
    }

    private async ValueTask<(byte[] ImageBytes, string Extension)> DownloadProgramImageAsync(string imageUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", config.ExternalServiceUserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await CreateHttpClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"画像取得に失敗しました。status={(int)response.StatusCode}");
        }

        var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (imageBytes.Length == 0)
        {
            throw new InvalidOperationException("画像データが空です。");
        }

        var extension = ResolveImageExtension(response.Content.Headers.ContentType?.MediaType, imageUrl);
        return (imageBytes, extension);
    }

    private static string ResolveImageExtension(string? contentType, string imageUrl)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            if (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase)) return ".jpg";
            if (contentType.Contains("png", StringComparison.OrdinalIgnoreCase)) return ".png";
            if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase)) return ".webp";
            if (contentType.Contains("gif", StringComparison.OrdinalIgnoreCase)) return ".gif";
            if (contentType.Contains("bmp", StringComparison.OrdinalIgnoreCase)) return ".bmp";
        }

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
        {
            var ext = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext) && ext.Length <= 5)
            {
                return ext.ToLowerInvariant();
            }
        }

        return ".img";
    }

    private HttpClient CreateHttpClient()
    {
        return httpClientFactory?.CreateClient(HttpClientNames.Radiko) ?? new HttpClient();
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 削除失敗時も録音フローは継続
        }
    }
}
