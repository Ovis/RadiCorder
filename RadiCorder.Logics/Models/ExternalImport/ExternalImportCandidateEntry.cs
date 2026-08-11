namespace RadiCorder.Logics.Models.ExternalImport;

/// <summary>
/// 外部取込候補の編集エントリ
/// </summary>
public class ExternalImportCandidateEntry
{
    /// <summary>
    /// 取込対象かどうか
    /// </summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>
    /// 音声ファイルのフルパス
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 番組タイトル
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 番組説明
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 放送局名
    /// </summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>
    /// 放送日時
    /// </summary>
    public DateTimeOffset BroadcastAt { get; set; }

    /// <summary>
    /// タグ名一覧
    /// </summary>
    public List<string> Tags { get; set; } = [];
}
