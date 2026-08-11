namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音ソースの解決結果
/// </summary>
/// <param name="StreamUrl">ストリームURL（タイムフリーの場合はplaylist_create_urlのベース）</param>
/// <param name="Headers">HTTPヘッダー</param>
/// <param name="ProgramInfo">番組情報</param>
/// <param name="Options">録音オプション</param>
/// <param name="RequestStationIdOverride">radiko向けの実リクエスト用station_id上書き</param>
public record RecordingSourceResult(
    string StreamUrl,
    IReadOnlyDictionary<string, string> Headers,
    ProgramRecordingInfo ProgramInfo,
    RecordingOptions Options,
    string? RequestStationIdOverride = null);
