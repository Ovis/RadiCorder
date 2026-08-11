namespace RadiCorder.Logics.Models;

/// <summary>
/// 類似録音抽出ジョブの実行状態
/// </summary>
public class RecordedDuplicateDetectionStatusEntry
{
    public bool IsRunning { get; set; }
    public DateTimeOffset? LastStartedAtUtc { get; set; }
    public DateTimeOffset? LastCompletedAtUtc { get; set; }
    public bool LastSucceeded { get; set; }
    public string LastMessage { get; set; } = string.Empty;
}
