namespace RadiCorder.Logics.Models;

/// <summary>
/// タグ一括操作結果
/// </summary>
public class TagBulkOperationResult
{
    public int SuccessCount { get; set; }
    public int SkipCount { get; set; }
    public int FailCount { get; set; }
    public List<string> FailedRecordingIds { get; set; } = [];
}
