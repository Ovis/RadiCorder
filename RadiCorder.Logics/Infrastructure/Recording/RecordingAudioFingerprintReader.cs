using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.DuplicateDetection;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// ffmpegで音声を読み出し、比較用の音声指紋を取得する。
/// </summary>
public class RecordingAudioFingerprintReader(ILogger logger, IAppConfigurationService config, IConfiguration configuration)
{
    private readonly string _ffmpegPath = ResolveFfmpegPath(configuration, config);
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_ffmpegPath) && File.Exists(_ffmpegPath);

    public async ValueTask<double[]> GetFingerprintAsync(
        DuplicateRecording recording,
        IDictionary<Ulid, double[]> cache,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(recording.RecordingId, out var cached))
        {
            return cached;
        }

        var path = ResolveRecordingPath(recording);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            logger.ZLogWarning($"類似抽出: 録音ファイルが見つかりません。recordingId={recording.RecordingId} path={path}");
            cache[recording.RecordingId] = [];
            return [];
        }

        var sampleSeconds = (int)Math.Clamp(recording.DurationSeconds * 0.6d, 180d, 900d);
        sampleSeconds = Math.Min(sampleSeconds, (int)Math.Max(recording.DurationSeconds - 2d, 30d));
        var startSeconds = Math.Max(0d, (recording.DurationSeconds - sampleSeconds) / 2d);

        var args =
            $"-hide_banner -loglevel error -nostdin -ss {startSeconds.ToString("0.###", CultureInfo.InvariantCulture)} -i \"{path}\" -t {sampleSeconds.ToString(CultureInfo.InvariantCulture)} -vn -ac 1 -ar {DuplicateSimilarity.AudioSampleRate} -f s16le -";

        try
        {
            var (exitCode, stdout, stderr) = await ExecuteProcessAsync(_ffmpegPath, args, cancellationToken);
            if (exitCode != 0 || stdout.Length == 0)
            {
                logger.ZLogWarning($"類似抽出: ffmpeg 解析失敗 recordingId={recording.RecordingId} exitCode={exitCode} stderr={stderr}");
                cache[recording.RecordingId] = [];
                return [];
            }

            var bins = DuplicateSimilarity.BuildEnergyBins(stdout);
            cache[recording.RecordingId] = bins;
            return bins;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"類似抽出: 音声指紋化に失敗 recordingId={recording.RecordingId}");
            cache[recording.RecordingId] = [];
            return [];
        }
    }

    private string ResolveRecordingPath(DuplicateRecording recording)
    {
        if (config.RecordFileSaveDir.TryCombinePaths(recording.FileRelativePath, out var fullPath))
        {
            return fullPath;
        }

        return string.Empty;
    }

    private static async ValueTask<(int ExitCode, byte[] StdOut, string StdErr)> ExecuteProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
        var stdOutTask = ReadAllBytesAsync(process.StandardOutput.BaseStream, CancellationToken.None).AsTask();
        var stdErrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            await Task.WhenAll(stdOutTask, stdErrTask);
        }
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        return (process.ExitCode, stdOut, stdErr);
    }

    private static async ValueTask<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private static string ResolveFfmpegPath(IConfiguration configuration, IAppConfigurationService config)
    {
        var configuredPath = config.FfmpegExecutablePath;
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var bundledCandidates = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe")
        };

        foreach (var candidate in bundledCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var fromPath = FindByCommand("where", "ffmpeg.exe");
            if (!string.IsNullOrWhiteSpace(fromPath))
            {
                return fromPath;
            }
        }
        else
        {
            var fromPath = FindByCommand("which", "ffmpeg");
            if (!string.IsNullOrWhiteSpace(fromPath))
            {
                return fromPath;
            }
        }

        var fallback = configuration["RadiCorder:FfmpegExecutablePath"];
        return fallback ?? string.Empty;
    }

    private static string FindByCommand(string command, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadLine();
            process.WaitForExit();
            return output ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
