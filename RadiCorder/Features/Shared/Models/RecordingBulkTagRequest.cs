namespace RadiCorder.Features.Shared.Models;

/// <summary>
/// 録音タグ一括操作リクエスト
/// </summary>
public class RecordingBulkTagRequest
{
    public List<string> RecordingIds { get; set; } = [];
    public List<Guid> TagIds { get; set; } = [];
}

