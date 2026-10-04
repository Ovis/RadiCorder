using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Extensions;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

internal class RecordingFfmpegRunner(ILogger<MediaTranscodeService> logger, IFfmpegService ffmpegService)
{
    private const int NonRealtimeRetryMaxAttempts = 3;
    private const int NonRealtimeRetryInitialDelaySeconds = 30;

    public async ValueTask<bool> RunFfmpegWithRetryAsync(
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
}
