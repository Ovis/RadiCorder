namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音ファイルのパス情報
/// </summary>
/// <param name="TempFilePath">一時ファイルのフルパス</param>
/// <param name="FinalFilePath">最終保存先のフルパス</param>
/// <param name="RelativePath">保存先の相対パス</param>
public record MediaPath(
    string TempFilePath,
    string FinalFilePath,
    string RelativePath);
