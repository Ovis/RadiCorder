namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音処理の状態
/// </summary>
public enum RecordingState
{
    /// <summary>未処理</summary>
    Pending = 0,
    /// <summary>録音中</summary>
    Recording = 1,
    /// <summary>完了</summary>
    Completed = 2,
    /// <summary>失敗</summary>
    Failed = 3,
    /// <summary>中断</summary>
    Aborted = 4
}
