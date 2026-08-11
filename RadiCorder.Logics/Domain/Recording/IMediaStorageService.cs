namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音ファイルの保存/移動/掃除を担うサービス
/// </summary>
public interface IMediaStorageService
{
    /// <summary>
    /// 保存先パスを準備する
    /// </summary>
    /// <param name="programInfo">番組情報</param>
    /// <param name="options">録音オプション</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>パス情報</returns>
    ValueTask<MediaPath> PrepareAsync(ProgramRecordingInfo programInfo, RecordingOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一時ファイルを最終保存先へ確定させる
    /// </summary>
    /// <param name="path">パス情報</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>確定後の最終パス情報</returns>
    ValueTask<MediaPath> CommitAsync(MediaPath path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一時ファイルを削除する
    /// </summary>
    /// <param name="path">パス情報</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask CleanupTempAsync(MediaPath path, CancellationToken cancellationToken = default);

    /// <summary>
    /// 最終保存に失敗した一時ファイルを退避保存する
    /// </summary>
    /// <param name="path">パス情報</param>
    /// <param name="metadata">退避時メタ情報</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    ValueTask<SaveFailedFallbackResult> SaveFailedAsync(
        MediaPath path,
        SaveFailedFallbackMetadata metadata,
        CancellationToken cancellationToken = default);
}
