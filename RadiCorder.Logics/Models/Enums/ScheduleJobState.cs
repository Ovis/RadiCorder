namespace RadiCorder.Logics.Models.Enums;

/// <summary>
/// 録音予約ジョブの実行状態
/// </summary>
public enum ScheduleJobState
{
    /// <summary>
    /// 実行待機
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 実行キュー投入済み
    /// </summary>
    Queued = 1,

    /// <summary>
    /// 録音準備中
    /// </summary>
    Preparing = 2,

    /// <summary>
    /// 録音中
    /// </summary>
    Recording = 3,

    /// <summary>
    /// 後処理中
    /// </summary>
    Finalizing = 4,

    /// <summary>
    /// 完了
    /// </summary>
    Completed = 5,

    /// <summary>
    /// 失敗
    /// </summary>
    Failed = 6,

    /// <summary>
    /// キャンセル
    /// </summary>
    Cancelled = 7
}
