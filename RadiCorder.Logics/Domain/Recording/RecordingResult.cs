namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音処理の結果
/// </summary>
/// <param name="IsSuccess">成功可否</param>
/// <param name="RecordingId">録音ID</param>
/// <param name="ErrorMessage">エラーメッセージ</param>
public record RecordingResult(
    bool IsSuccess,
    Ulid? RecordingId,
    string? ErrorMessage = null)
{
    public RadiCorder.Logics.Models.Enums.ScheduleJobErrorCode ErrorCode { get; init; } = RadiCorder.Logics.Models.Enums.ScheduleJobErrorCode.Unknown;
}
