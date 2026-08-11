namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音データの作成元種別
/// </summary>
public enum RecordingSourceType
{
    /// <summary>
    /// 通常録音
    /// </summary>
    Recorded = 0,

    /// <summary>
    /// 外部ファイル取込
    /// </summary>
    ExternalImport = 1
}
