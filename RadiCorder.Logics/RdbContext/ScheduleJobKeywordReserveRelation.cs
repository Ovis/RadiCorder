namespace RadiCorder.Logics.RdbContext;

/// <summary>
/// 録音スケジュールとキーワード予約ルールの関連
/// </summary>
public class ScheduleJobKeywordReserveRelation
{
    public Ulid ScheduleJobId { get; set; }

    public Ulid KeywordReserveId { get; set; }

    public ScheduleJob ScheduleJob { get; set; } = null!;

    public KeywordReserve KeywordReserve { get; set; } = null!;
}

