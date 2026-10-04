using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Errors;

/// <summary>
/// 表示文言とは独立した録音失敗の分類を保持する。
/// </summary>
public class RecordingFailureException(ScheduleJobErrorCode errorCode, string message, Exception? innerException = null) : DomainException(message, innerException)
{
    public ScheduleJobErrorCode ErrorCode { get; } = errorCode;
}
