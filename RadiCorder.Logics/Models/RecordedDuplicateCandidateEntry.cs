namespace RadiCorder.Logics.Models;

/// <summary>
/// 類似録音候補の片側情報
/// </summary>
public class RecordedDuplicateSideEntry
{
    public string RecordingId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public DateTimeOffset StartDateTime { get; set; }
    public DateTimeOffset EndDateTime { get; set; }
    public double DurationSeconds { get; set; }
}

/// <summary>
/// 類似録音候補
/// </summary>
public class RecordedDuplicateCandidateEntry
{
    public RecordedDuplicateSideEntry Left { get; set; } = new();
    public RecordedDuplicateSideEntry Right { get; set; } = new();
    public double Phase1Score { get; set; }
    public double AudioScore { get; set; }
    public double FinalScore { get; set; }
    public double StartTimeDiffHours { get; set; }
    public double DurationDiffSeconds { get; set; }
}
