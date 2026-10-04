using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Extensions;
using ZLogger;

namespace RadiCorder.Logics.Services
{
    public class FfmpegService(
        ILogger<IFfmpegService> logger,
        IAppConfigurationService appConfigurationService,
        IConfiguration configuration) : IFfmpegService
    {
        private string? _ffmpegPath;
        private readonly object _logFileLock = new();
        private readonly string _logDirectory = ResolveLogDirectory(configuration);

        /// <summary>
        /// Ffmpegのパス
        /// </summary>
        private string ExecutablePath => _ffmpegPath ??= GetFfmpegPath();


        public bool Initialize()
        {
            return GetFfmpegPath() != string.Empty;
        }

        private string GetFfmpegPath()
        {
            var configuredPath = appConfigurationService.FfmpegExecutablePath;
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            // 実行ディレクトリ直下の同梱ffmpegを優先（OS差異を吸収）
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

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetProgramPathWindows();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                return GetProgramPathLinux();
            }
            else
            {
                throw new PlatformNotSupportedException("This platform is not supported.");
            }
        }

        private string GenerateLoggingFileName(string fileName)
        {
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            var safeFileName = fileName.ToSafeLogFileName();
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                safeFileName = $"ffmpeg-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            }

            return Path.Combine(_logDirectory, $"{safeFileName}.log");
        }


        /// <summary>
        /// Ffmpegプロセスを実行
        /// </summary>
        /// <param name="arguments">FFmpegに渡す引数</param>
        /// <param name="timeoutSeconds">タイムアウト時間（秒）</param>
        /// <param name="loggingProgramName">ログファイル名（空文字の場合はログファイルに書き込まない）</param>
        /// <param name="cancellationToken"></param>
        /// <returns>処理が成功したかどうか</returns>
        public async ValueTask<bool> RunProcessAsync(
            string arguments,
            int timeoutSeconds,
            string loggingProgramName = "",
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource();
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                var loggingFilePath = string.Empty;
                if (!string.IsNullOrEmpty(loggingProgramName))
                {
                    try { loggingFilePath = GenerateLoggingFileName(loggingProgramName); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.ZLogWarning(ex, $"FFmpegログファイルを作成できません。録音処理は継続します。");
                    }
                }

                var result = await ExecuteFfmpegTaskAsync(arguments, linkedCts.Token, loggingFilePath);

                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException e)
            {
                logger.ZLogError(e, $"タイムアウト: FFmpegプロセスが指定時間内に終了しませんでした。");

                return false;
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"Ffmpeg処理でエラーが発生しました");

                return false;
            }
        }

        private string GetProgramPathWindows()
        {
            return FindExecutablePath("where", "ffmpeg.exe");
        }

        private string GetProgramPathLinux()
        {
            return FindExecutablePath("which", "ffmpeg");
        }

        private string FindExecutablePath(string finderCommand, string finderArgument)
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = finderCommand;
                process.StartInfo.Arguments = finderArgument;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();

                var output = process.StandardOutput.ReadLine();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    return output;
                }
            }
            catch (Exception ex)
            {
                logger.ZLogError($"Error finding ffmpeg: {ex.Message}");
                throw new FileNotFoundException("ffmpeg not found.");
            }

            return string.Empty;
        }

        private async ValueTask<bool> ExecuteFfmpegTaskAsync(string arguments, CancellationToken token, string logFilePath)
        {
            using var process = new Process();

            process.StartInfo.FileName = ExecutablePath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            var logEnabled = !string.IsNullOrEmpty(logFilePath);
            var outputTask = ReadOutputAsync(process.StandardOutput, "[FFmpeg Output] ");
            var errorTask = ReadOutputAsync(process.StandardError, string.Empty);
            try
            {
                await process.WaitForExitAsync(token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                // 終了時にも出力を読み切り、バックグラウンドの読み取り処理を残さない。
                await Task.WhenAll(outputTask, errorTask);
            }

            if (process.ExitCode != 0)
            {
                logger.ZLogError($"FFmpegプロセスが異常終了しました。exitCode={process.ExitCode}");
            }
            return process.ExitCode == 0;

            async Task ReadOutputAsync(StreamReader reader, string prefix)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    logger.ZLogDebug($"{prefix}{line}");
                    lock (_logFileLock)
                    {
                        if (!logEnabled) continue;
                        try
                        {
                            File.AppendAllText(logFilePath, $"{prefix}{line}{Environment.NewLine}", Encoding.UTF8);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            logEnabled = false;
                            logger.ZLogWarning(ex, $"FFmpegログの書き込みに失敗しました。録音処理は継続します。");
                        }
                    }
                }
            }
        }

        private static string ResolveLogDirectory(IConfiguration configuration)
        {
            var configured = configuration["RadiCorder:LogDirectory"];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (Path.IsPathRooted(configured))
                {
                    return configured;
                }

                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        }
    }
}
