using RadiCorder.Logics.Primitives.DataAnnotations;

namespace RadiCorder.Logics.Logics.NotificationLogic
{
    public enum NoticeCategory
    {
        [EnumDisplayName("未定義")]
        Undefined = 0,

        [EnumDisplayName("録音開始")]
        RecordingStart = 100,

        [EnumDisplayName("録音成功")]
        RecordingSuccess = 110,

        [EnumDisplayName("録音失敗")]
        RecordingError = 120,

        [EnumDisplayName("録音キャンセル")]
        RecordingCancel = 130,

        [EnumDisplayName("番組表更新開始")]
        UpdateProgramStart = 200,

        [EnumDisplayName("番組表更新終了")]
        UpdateProgramEnd = 210,

        [EnumDisplayName("番組表更新失敗")]
        UpdateProgramError = 220,

        [EnumDisplayName("キーワード予約失敗")]
        KeywordReserveError = 300,

        [EnumDisplayName("ディスク容量不足")]
        StorageLowSpace = 400,

        [EnumDisplayName("時刻ずれ検知")]
        ClockSkewDetected = 410,

        [EnumDisplayName("新しいリリース")]
        NewRelease = 500,

        [EnumDisplayName("類似番組抽出")]
        DuplicateProgramDetection = 510,

        [EnumDisplayName("システムエラー")]
        SystemError = 999,
    }
}
