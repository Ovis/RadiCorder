namespace RadiCorder.Features.Shared.Models;

/// <summary>
/// 類似録音抽出の手動実行オプション
/// </summary>
public class DuplicateDetectionRunRequest
{
    /// <summary>
    /// 手動実行時の抽出対象期間(日数)。0以下は全件。
    /// </summary>
    public int LookbackDays { get; set; } = 30;

    /// <summary>
    /// 2段目判定に進める同一番組候補グループ上限数。
    /// </summary>
    public int MaxPhase1Groups { get; set; } = 100;

    /// <summary>
    /// 2段目判定の比較モード。light / strict
    /// </summary>
    public string Phase2Mode { get; set; } = "light";

    /// <summary>
    /// 放送回クラスタ分割の時間窓（時間）。
    /// </summary>
    public int BroadcastClusterWindowHours { get; set; } = 48;
}

