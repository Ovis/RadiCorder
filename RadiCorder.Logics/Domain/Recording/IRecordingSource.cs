using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.Recording;

/// <summary>
/// 録音ソース取得の抽象化
/// </summary>
public interface IRecordingSource
{
    /// <summary>
    /// 指定されたサービス種別を処理可能か判定する
    /// </summary>
    /// <param name="kind">配信サービス種別</param>
    /// <returns>処理可能ならtrue</returns>
    bool CanHandle(RadioServiceKind kind);

    /// <summary>
    /// 録音に必要なストリームURLと番組情報を取得する
    /// </summary>
    /// <param name="command">録音コマンド</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>録音ソース情報</returns>
    ValueTask<RecordingSourceResult> PrepareAsync(RecordingCommand command, CancellationToken cancellationToken = default);
}
