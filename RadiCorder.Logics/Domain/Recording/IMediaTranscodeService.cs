namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音（FFmpeg等）を実行するサービス
/// </summary>
public interface IMediaTranscodeService
{
    /// <summary>
    /// 録音を実行する
    /// </summary>
    /// <param name="source">録音ソース情報</param>
    /// <param name="path">保存先パス</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>成功時true</returns>
    ValueTask<bool> RecordAsync(RecordingSourceResult source, MediaPath path, CancellationToken cancellationToken = default);
}
