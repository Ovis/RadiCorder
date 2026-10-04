using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Providers;

/// <summary>
/// 取得計画を渡さない既存の呼び出し側に限り、従来のオプションを変換する。
/// </summary>
internal static class LegacyRecordingAcquisitionPlan
{
    public static RecordingAcquisitionPlan FromOptions(RecordingOptions options)
        => new(options.IsOnDemand ? RecordingAcquisitionPlan.Archive : options.IsTimeFree
            ? options.ServiceKind == RadioServiceKind.Radiko ? RecordingAcquisitionPlan.RadikoTimeFree : "unsupported"
            : RecordingAcquisitionPlan.Live, FastStart: options.ServiceKind == RadioServiceKind.Radiru,
            TailCompensationSeconds: options.ServiceKind == RadioServiceKind.Radiko ? 10 : 0);
}
