using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 既存ジョブの失敗分類規則を提供する。
/// </summary>
public static class RecordingJobErrorClassifier
{
    /// <summary>
    /// 例外を失敗分類へ変換する。
    /// </summary>
    public static ScheduleJobErrorCode ClassifyError(Exception? exception)
    {
        if (exception == null)
        {
            return ScheduleJobErrorCode.Unknown;
        }

        if (exception is OperationCanceledException)
        {
            return ScheduleJobErrorCode.Cancelled;
        }

        var message = exception.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            return ScheduleJobErrorCode.Unknown;
        }

        if (message.Contains("認証", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.AuthFailed;
        }

        if (message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.FfmpegFailed;
        }

        if (message.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("容量", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.DiskFull;
        }

        if (message.Contains("io", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("I/O", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.IoError;
        }

        if (message.Contains("source", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("playlist", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("配信", StringComparison.OrdinalIgnoreCase))
        {
            return ScheduleJobErrorCode.SourceUnavailable;
        }

        return ScheduleJobErrorCode.Unknown;
    }
}
