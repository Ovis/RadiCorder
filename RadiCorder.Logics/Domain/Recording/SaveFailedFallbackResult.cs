namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 最終保存失敗時の退避保存結果
/// </summary>
public record SaveFailedFallbackResult(
    string FilePath,
    string? MetadataPath);

