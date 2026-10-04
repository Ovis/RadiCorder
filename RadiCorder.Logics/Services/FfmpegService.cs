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

                var loggingFilePath = string.IsNullOrEmpty(loggingProgramName) ? string.Empty : GenerateLoggingFileName(loggingProgramName);

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

            try
            {
                process.OutputDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    if (!string.IsNullOrEmpty(logFilePath))
                    {
                        lock (_logFileLock)
                        {
                            using var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                            using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                            writer.WriteLine($"[FFmpeg Output] {e.Data}");
                        }
                    }
                    logger.ZLogDebug($"[FFmpeg Output] {e.Data}");
                };

                process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    if (!string.IsNullOrEmpty(logFilePath))
                    {
                        lock (_logFileLock)
                        {
                            using var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                            using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                            writer.WriteLine($"{e.Data}");
                        }
                    }
                    logger.ZLogDebug($"{e.Data}");
                };

                process.Start();

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var taskCompletionSource = new TaskCompletionSource<bool>();

                process.Exited += (_, _) =>
                {
                    var isSuccess = process.ExitCode == 0;
                    taskCompletionSource.TrySetResult(isSuccess);
                };
                process.EnableRaisingEvents = true;

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var delayTask = Task.Delay(-1, linkedCts.Token);

                try
                {
                    var completedTask = await Task.WhenAny(taskCompletionSource.Task, delayTask);

                    if (completedTask == taskCompletionSource.Task)
                    {
                        var isSuccess = await taskCompletionSource.Task;
                        if (!isSuccess)
                        {
                            logger.ZLogError($"FFmpegプロセスが異常終了しました。exitCode={process.ExitCode}");
                        }

                        return isSuccess;
                    }
                    else
                    {
                        if (!process.HasExited)
                        {
                            logger.ZLogError($"FFmpegプロセスが指定時間内に終了しませんでした。");
                            try
                            {
                                process.Kill();
                            }
                            catch (Exception ex)
                            {
                                logger.ZLogError(ex, $"FFmpegプロセスの強制終了に失敗しました。");
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        return false;
                    }
                }
                finally
                {
                    linkedCts.Cancel();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"Ffmpeg録音処理でエラー");
                return false;
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
