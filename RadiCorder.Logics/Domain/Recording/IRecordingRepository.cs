namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音結果の永続化を担うリポジトリ
/// </summary>
public interface IRecordingRepository
{
    /// <summary>
    /// 録音レコードを作成する
    /// </summary>
    /// <param name="programInfo">番組情報</param>
    /// <param name="path">保存先パス</param>
    /// <param name="options">録音オプション</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>録音ID</returns>
    ValueTask<Ulid> CreateAsync(ProgramRecordingInfo programInfo, MediaPath path, RecordingOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音状態を更新する
    /// </summary>
    /// <param name="recordingId">録音ID</param>
    /// <param name="state">状態</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask UpdateStateAsync(Ulid recordingId, RecordingState state, string? errorMessage = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音ファイルパスを更新する
    /// </summary>
    /// <param name="recordingId">録音ID</param>
    /// <param name="path">確定済みファイルパス</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask UpdateFilePathAsync(Ulid recordingId, MediaPath path, CancellationToken cancellationToken = default);
}
