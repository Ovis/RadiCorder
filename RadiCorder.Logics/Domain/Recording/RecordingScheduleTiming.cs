using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音種別に応じた開始時刻を算出する。現在時刻は呼び出し側で固定する。
/// </summary>
public static class RecordingScheduleTiming
{
    public static readonly TimeSpan PreparingLeadTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TimeFreeReadyLeadTime = TimeSpan.FromMinutes(3);

    public static DateTimeOffset? ResolveFireAtUtc(
        RecordingType recordingType,
        DateTimeOffset startDateTime,
        DateTimeOffset endDateTime,
        TimeSpan startDelay,
        DateTimeOffset nowUtc)
    {
        var timeFreeReadyAtUtc = endDateTime.ToUniversalTime().Add(TimeFreeReadyLeadTime);
        return recordingType switch
        {
            RecordingType.TimeFree => timeFreeReadyAtUtc > nowUtc ? timeFreeReadyAtUtc : nowUtc,
            RecordingType.OnDemand => nowUtc,
            RecordingType.Immediate => nowUtc,
            RecordingType.RealTime => startDateTime.AddSeconds(-startDelay.TotalSeconds).AddSeconds(-1).ToUniversalTime(),
            _ => null
        };
    }
}
