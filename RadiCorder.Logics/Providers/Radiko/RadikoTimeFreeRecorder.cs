using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Extensions;
using ZLogger;

namespace RadiCorder.Logics.Providers.Radiko;

/// <summary>
/// radikoの分割取得・結合方式。共通トランスコード処理から独立して変更する。
/// </summary>
public class RadikoTimeFreeRecorder(ILogger<MediaTranscodeService> logger, IFfmpegService ffmpegService,
    IAppConfigurationService config) : IRecordingAcquisitionMethod
{
    private const int TimeFreeChunkSecondsMax = 300;
    private const int TimeFreeChunkUnitSeconds = 5;
    private static readonly Encoding FileListEncoding = new UTF8Encoding(false);
    private readonly RecordingFfmpegRunner _runner = new(logger, ffmpegService);
    public string Method => RecordingAcquisitionPlan.RadikoTimeFree;

    public async ValueTask<bool> RecordAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken)
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

                var command = new FfmpegCommandBuilder();
                command.Append(" -nostdin -loglevel error -stats");
                command.Append(" -fflags +discardcorrupt");
                RecordingFfmpegArguments.AppendUserAgent(command, config.ExternalServiceUserAgent);
                RecordingFfmpegArguments.AppendHeaders(command, source.Headers);
                command.Append(" -http_seekable 0 -seekable 0");
                command.Add("-i", url);
                command.Append(" -acodec copy -vn -bsf:a aac_adtstoasc -y");
                command.Add(chunkFile);

                logger.ZLogDebug($"タイムフリー録音チャンク開始: chunk={chunkNo} seek={seek} end_at={endAt} l={chunkSeconds}s");

                var timeoutSeconds = Math.Clamp(chunkSeconds + 120, 120, 3600);
                if (!await _runner.RunFfmpegWithRetryAsync(
                    operationName: $"タイムフリー録音チャンク取得(chunk={chunkNo})",
                    ffmpegArguments: command.Build(),
                    timeoutSeconds: timeoutSeconds,
                    loggingProgramName: $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}_chunk{chunkNo}",
                    cancellationToken: cancellationToken))
                {
                    ok = false;
                    break;
                }

                var chunkForList = Path.GetFullPath(chunkFile).Replace('\\', '/');
                await File.AppendAllTextAsync(fileListPath, "file '" + chunkForList.Replace("'", "'\\''") + "'\n", FileListEncoding, cancellationToken);

                seekTime = seekTime.AddSeconds(chunkSeconds);
                leftSeconds -= chunkSeconds;
                chunkNo++;
            }

            if (!ok)
            {
                logger.ZLogError($"タイムフリー録音チャンク取得に失敗しました。");
                return false;
            }

            var concatCommand = new FfmpegCommandBuilder();
            concatCommand.Append(" -loglevel error -f concat -safe 0");
            concatCommand.Add("-i", fileListPath);
            concatCommand.Append(" -c copy");
            RecordingFfmpegArguments.AppendProgramInfo(concatCommand, source.ProgramInfo);
            concatCommand.Add("-y", path.TempFilePath);

            logger.ZLogDebug($"タイムフリー録音結合開始: station={source.ProgramInfo.StationId} title={source.ProgramInfo.Title} start={source.ProgramInfo.StartTime:O} end={source.ProgramInfo.EndTime:O}");
            return await _runner.RunFfmpegWithRetryAsync(
                operationName: "タイムフリー録音結合",
                ffmpegArguments: concatCommand.Build(),
                timeoutSeconds: 600,
                loggingProgramName: $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{source.ProgramInfo.Title}_concat",
                cancellationToken: cancellationToken);
        }
        finally
        {
            CleanupTempFiles(tmpDir, baseName);
        }
    }

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

    private static int GetTimeFreeChunkSeconds(int leftSeconds)
    {
        if (leftSeconds <= 0) return 0;
        if (leftSeconds >= TimeFreeChunkSecondsMax) return TimeFreeChunkSecondsMax;

        return leftSeconds % TimeFreeChunkUnitSeconds == 0
            ? leftSeconds
            : ((leftSeconds / TimeFreeChunkUnitSeconds) + 1) * TimeFreeChunkUnitSeconds;
    }

    private static string ToRadikoTimeString(DateTimeOffset dateTimeOffset)
    {
        return dateTimeOffset.ToJapanDateTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
    }

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
}
