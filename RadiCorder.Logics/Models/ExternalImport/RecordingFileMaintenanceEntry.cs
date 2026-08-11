namespace RadiCorder.Logics.Models.ExternalImport;

/// <summary>
/// 録音ファイル整合性メンテナンス対象エントリ
/// </summary>
public class RecordingFileMaintenanceEntry
{
    /// <summary>
    /// 問題種別（missing_file / failed_with_existing_file）
    /// </summary>
    public string IssueType { get; set; } = "missing_file";

    /// <summary>
    /// 録音ID
    /// </summary>
    public string RecordingId { get; set; } = string.Empty;

    /// <summary>
    /// 番組タイトル
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 放送局名
    /// </summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>
    /// DBに保存されているファイルパス
    /// </summary>
    public string StoredPath { get; set; } = string.Empty;

    /// <summary>
    /// ファイル名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// 同名候補ファイル数
    /// </summary>
    public int CandidateCount { get; set; }

    /// <summary>
    /// 同名候補の相対パス一覧（表示用）
    /// </summary>
    public List<string> CandidateRelativePaths { get; set; } = [];
}

/// <summary>
/// 録音ファイル整合性スキャン結果
/// </summary>
public class RecordingFileMaintenanceScanResult
{
    /// <summary>
    /// 欠損レコード数
    /// </summary>
    public int MissingCount { get; set; }

    /// <summary>
    /// ファイル実在のFailedレコード数
    /// </summary>
    public int RecoverableFailedCount { get; set; }

    /// <summary>
    /// 欠損レコード一覧
    /// </summary>
    public List<RecordingFileMaintenanceEntry> Entries { get; set; } = [];
}

/// <summary>
/// 録音ファイル整合性メンテナンスの明細
/// </summary>
public class RecordingFileMaintenanceActionDetail
{
    /// <summary>
    /// 録音ID
    /// </summary>
    public string RecordingId { get; set; } = string.Empty;

    /// <summary>
    /// 処理結果
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// 処理結果メッセージ
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 録音ファイル整合性メンテナンス実行結果
/// </summary>
public class RecordingFileMaintenanceActionResult
{
    /// <summary>
    /// 対象件数
    /// </summary>
    public int TargetCount { get; set; }

    /// <summary>
    /// 成功件数
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// スキップ件数
    /// </summary>
    public int SkipCount { get; set; }

    /// <summary>
    /// 失敗件数
    /// </summary>
    public int FailCount { get; set; }

    /// <summary>
    /// 処理明細
    /// </summary>
    public List<RecordingFileMaintenanceActionDetail> Details { get; set; } = [];
}
