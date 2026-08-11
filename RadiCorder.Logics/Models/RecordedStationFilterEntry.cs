namespace RadiCorder.Logics.Models;

/// <summary>
/// 録音番組一覧の放送局フィルタ候補
/// </summary>
public class RecordedStationFilterEntry
{
    /// <summary>
    /// 放送局ID
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// 放送局表示名
    /// </summary>
    public string StationName { get; set; } = string.Empty;
}

