namespace RadiCorder.Logics.Domain.DuplicateDetection;

/// <summary>
/// 重複候補の比較に必要な録音情報。DBや音声fileを参照しない。
/// </summary>
public sealed record DuplicateRecording
{
    public Ulid RecordingId { get; init; }
    public string StationId { get; init; } = string.Empty;
    public string StationName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string NormalizedTitle { get; init; } = string.Empty;
    public DateTimeOffset StartDateTime { get; init; }
    public DateTimeOffset EndDateTime { get; init; }
    public double DurationSeconds { get; init; }
    public string FileRelativePath { get; init; } = string.Empty;
}

public sealed class DuplicatePair
{
    public required DuplicateRecording Left { get; init; }
    public required DuplicateRecording Right { get; init; }
    public required double Phase1Score { get; init; }
}

public sealed class DuplicateGroup
{
    public HashSet<Ulid> MemberIds { get; } = [];
    public List<DuplicatePair> Pairs { get; } = [];
    public double MaxPhase1Score { get; set; }
}
