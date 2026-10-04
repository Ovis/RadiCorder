using RadiCorder.Logics.Providers;
using RadiCorder.Logics.Providers.Radiko;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Extensions;
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
    IHttpClientFactory? httpClientFactory = null,
    IEnumerable<IRecordingAcquisitionMethod>? acquisitionMethods = null) : IMediaTranscodeService
{
    private readonly RecordingFfmpegRunner _runner = new(logger, ffmpegService);
    private readonly IReadOnlyDictionary<string, IRecordingAcquisitionMethod> _methods =
        (acquisitionMethods ?? [new RadikoTimeFreeRecorder(logger, ffmpegService, config)]).ToDictionary(x => x.Method, StringComparer.Ordinal);

    /// <summary>
    /// 録音を実行する
    /// </summary>
    public async ValueTask<bool> RecordAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken = default)
    {
        var plan = source.AcquisitionPlan ?? LegacyRecordingAcquisitionPlan.FromOptions(source.Options);
        source = source with { AcquisitionPlan = plan };
        var recorded = plan.Method switch
        {
            RecordingAcquisitionPlan.Live => await RecordRealTimeAsync(source, path, cancellationToken),
            RecordingAcquisitionPlan.Archive => await RecordOnDemandAsync(source, path, cancellationToken),
            _ => _methods.TryGetValue(plan.Method, out var method) && await method.RecordAsync(source, path, cancellationToken)
        };

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
        RecordingFfmpegArguments.AppendUserAgent(command, config.ExternalServiceUserAgent);
        RecordingFfmpegArguments.AppendHeaders(command, source.Headers);
        command.Append(" -http_seekable 0 -seekable 0");
        command.Append($" -i \"{source.StreamUrl}\"");
        RecordingFfmpegArguments.AppendAudio(command, source.AcquisitionPlan!);
        RecordingFfmpegArguments.AppendProgramInfo(command, source.ProgramInfo);
        command.Append($" -y \"{path.TempFilePath}\"");

        logger.ZLogDebug($"聞き逃し配信録音開始: station={source.ProgramInfo.StationId} title={source.ProgramInfo.Title} programId={source.ProgramInfo.ProgramId}");

        return await _runner.RunFfmpegWithRetryAsync(
            operationName: "聞き逃し配信録音",
            ffmpegArguments: command.ToString(),
            timeoutSeconds: timeout,
            loggingProgramName: $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}_ondemand",
            cancellationToken: cancellationToken);
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

        var tailCompensationSeconds = source.AcquisitionPlan!.TailCompensationSeconds;
        var diff = source.ProgramInfo.EndTime
            .AddSeconds(source.Options.StartDelaySeconds)
            .AddSeconds(source.Options.EndDelaySeconds)
            .AddSeconds(tailCompensationSeconds)
            - startTime;

        var command = new StringBuilder();
        // HLS ライブ入力は元から実時間で供給されるため、-re で入力を絞ると
        // ライブ窓から取りこぼしやすくなる。
        command.Append(" -vn -nostdin");
        RecordingFfmpegArguments.AppendUserAgent(command, config.ExternalServiceUserAgent);
        RecordingFfmpegArguments.AppendHeaders(command, source.Headers);
        command.Append(" -http_seekable 0 -seekable 0");
        command.Append(" -reconnect 1 -reconnect_streamed 1 -reconnect_on_network_error 1 -reconnect_delay_max 120");
        command.Append($" -i \"{source.StreamUrl}\"");
        command.Append($" -t {diff.TotalSeconds.ToString(CultureInfo.InvariantCulture)}");

        if (source.AcquisitionPlan!.FastStart)
        {
            // 取得計画で指定された出力の最適化
            command.Append(" -movflags +faststart");
        }

        RecordingFfmpegArguments.AppendAudio(command, source.AcquisitionPlan!);
        command.Append(" -y");
        RecordingFfmpegArguments.AppendProgramInfo(command, source.ProgramInfo);
        command.Append($" -y \"{path.TempFilePath}\"");

        var timeout = (int)diff.Add(new TimeSpan(0, 10, 0)).TotalSeconds;
        logger.ZLogDebug($"リアルタイム録音開始: station={source.ProgramInfo.StationId} title={source.ProgramInfo.Title} start={source.ProgramInfo.StartTime:O} end={source.ProgramInfo.EndTime:O} tailCompSec={tailCompensationSeconds} timeoutSec={timeout}");

        return await ffmpegService.RunProcessAsync(
            command.ToString(),
            timeout,
            $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}",
            cancellationToken);
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
