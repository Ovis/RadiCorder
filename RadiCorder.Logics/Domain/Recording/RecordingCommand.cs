using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音処理に必要な入力パラメータ
/// </summary>
/// <param name="ServiceKind">配信サービス種別</param>
/// <param name="ProgramId">番組ID</param>
/// <param name="ProgramName">番組名</param>
/// <param name="IsTimeFree">タイムフリー録音かどうか</param>
/// <param name="IsOnDemand">聞き逃し配信録音かどうか</param>
/// <param name="StartDelaySeconds">開始ディレイ（秒）</param>
/// <param name="EndDelaySeconds">終了ディレイ（秒）</param>
/// <param name="ScheduleJobId">関連するスケジュールジョブID（任意）</param>
public record RecordingCommand(
    RadioServiceKind ServiceKind,
    string ProgramId,
    string ProgramName,
    bool IsTimeFree,
    double StartDelaySeconds,
    double EndDelaySeconds,
    string? ScheduleJobId = null,
    bool IsOnDemand = false,
    string? OutputDirectoryRelativePathOverride = null,
    string? OutputFileNameTemplateOverride = null);
