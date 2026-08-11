namespace RadiCorder.Logics.Models;

/// <summary>
/// タグ情報
/// </summary>
public class TagEntry
{
    /// <summary>
    /// タグID
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// タグ名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 録音への利用件数
    /// </summary>
    public int RecordingCount { get; set; }

    /// <summary>
    /// 最終利用日時
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
