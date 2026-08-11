namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 最終保存に失敗した録音ファイルを退避保存する際のメタ情報
/// </summary>
public record SaveFailedFallbackMetadata(
    DateTimeOffset RecordedAt,
    string ProgramId,
    string StationId,
    string Title,
    string OriginalDestinationPath,
    string ErrorType,
    string ErrorMessage,
    IReadOnlyDictionary<string, string> ExpectedTags);

