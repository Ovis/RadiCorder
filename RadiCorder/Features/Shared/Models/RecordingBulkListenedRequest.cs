namespace RadiCorder.Features.Shared.Models;

/// <summary>
/// 録音番組の一括既読/未読更新リクエスト
/// </summary>
public class RecordingBulkListenedRequest
{
    /// <summary>
    /// 更新対象の録音ID
    /// </summary>
    public List<string> RecordingIds { get; set; } = [];

    /// <summary>
    /// true: 既読化 / false: 未読化
    /// </summary>
    public bool IsListened { get; set; }
}

