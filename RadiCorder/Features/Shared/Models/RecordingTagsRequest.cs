namespace RadiCorder.Features.Shared.Models;

/// <summary>
/// 録音タグ付与リクエスト
/// </summary>
public class RecordingTagsRequest
{
    public List<Guid> TagIds { get; set; } = [];
}

