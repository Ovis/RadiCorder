namespace RadiCorder.Logics.Models.Enums;

/// <summary>
/// 録音予約ジョブ失敗分類コード
/// </summary>
public enum ScheduleJobErrorCode
{
    /// <summary>
    /// エラーなし
    /// </summary>
    None = 0,

    /// <summary>
    /// 認証失敗
    /// </summary>
    AuthFailed = 1,

    /// <summary>
    /// 取得元ソース利用不可
    /// </summary>
    SourceUnavailable = 2,

    /// <summary>
    /// FFmpeg 実行失敗
    /// </summary>
    FfmpegFailed = 3,

    /// <summary>
    /// ディスク容量不足
    /// </summary>
    DiskFull = 4,

    /// <summary>
    /// I/O エラー
    /// </summary>
    IoError = 5,

    /// <summary>
    /// 後処理失敗
    /// </summary>
    FinalizeFailed = 6,

    /// <summary>
    /// 起動時復旧タイムアウト
    /// </summary>
    StartupRecoveryTimeout = 7,

    /// <summary>
    /// キャンセル
    /// </summary>
    Cancelled = 8,

    /// <summary>
    /// 不明
    /// </summary>
    Unknown = 99
}
