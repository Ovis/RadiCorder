namespace RadiCorder.Logics.Models.ExternalImport;

/// <summary>
/// 外部取込保存結果
/// </summary>
public class ExternalImportSaveResult
{
    /// <summary>
    /// 保存成功件数
    /// </summary>
    public int SavedCount { get; set; }

    /// <summary>
    /// エラー一覧
    /// </summary>
    public List<ExternalImportValidationError> Errors { get; set; } = [];
}

/// <summary>
/// 候補の検証エラー
/// </summary>
public class ExternalImportValidationError
{
    /// <summary>
    /// 対象ファイルパス
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
