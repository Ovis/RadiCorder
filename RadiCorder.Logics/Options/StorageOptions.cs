namespace RadiCorder.Logics.Options;

/// <summary>
/// 保存先や実行ファイル配置など、ローカルストレージ関連の設定。
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// 録音ファイル保存先フォルダ。
    /// </summary>
    public string RecordFileSaveFolder { get; set; } = string.Empty;

    /// <summary>
    /// 一時ファイル保存先フォルダ。
    /// </summary>
    public string TemporaryFileSaveFolder { get; set; } = string.Empty;

    /// <summary>
    /// ffmpeg 実行ファイルの絶対パス。未指定時は自動探索。
    /// </summary>
    public string FfmpegExecutablePath { get; set; } = string.Empty;
}

