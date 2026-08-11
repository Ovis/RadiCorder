using RadiCorder.Logics.Primitives.DataAnnotations;

namespace RadiCorder.Logics.Models.Enums
{
    /// <summary>
    /// 録音方法種別
    /// </summary>
    public enum RecordingType
    {
        [EnumDisplayName("未定義")]
        Undefined = 0,

        [EnumDisplayName("通常録音")]
        RealTime = 1,

        [EnumDisplayName("タイムフリー")]
        TimeFree = 2,

        [EnumDisplayName("即時実行")]
        Immediate = 3,

        [EnumDisplayName("聞き逃し配信")]
        OnDemand = 4,
    }
}
