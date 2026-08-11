using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音時の動作オプション
/// </summary>
/// <param name="ServiceKind">配信サービス種別</param>
/// <param name="IsTimeFree">タイムフリー録音かどうか</param>
/// <param name="IsOnDemand">聞き逃し配信録音かどうか</param>
/// <param name="StartDelaySeconds">開始ディレイ（秒）</param>
/// <param name="EndDelaySeconds">終了ディレイ（秒）</param>
public record RecordingOptions(
    RadioServiceKind ServiceKind,
    bool IsTimeFree,
    double StartDelaySeconds,
    double EndDelaySeconds,
    bool IsOnDemand = false,
    string? OutputDirectoryRelativePathOverride = null,
    string? OutputFileNameTemplateOverride = null);
