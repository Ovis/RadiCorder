namespace RadiCorder.Logics.Services;

/// <summary>
/// FFmpegを操作するためのサービスインターフェース
/// </summary>
public interface IFfmpegService
{
    /// <summary>
    /// FFmpegの初期化を行い、実行可能か確認します
    /// </summary>
    /// <returns>FFmpegが利用可能な場合はtrue、それ以外はfalse</returns>
    bool Initialize();

    /// <summary>
    /// FFmpegプロセスを実行します
    /// </summary>
    /// <param name="arguments">FFmpegに渡す引数</param>
    /// <param name="timeoutSeconds">タイムアウト時間（秒）</param>
    /// <param name="loggingProgramName">ログファイル名（空文字の場合はログファイルに書き込まない）</param>
    /// <param name="cancellationToken">外部キャンセルトークン</param>
    /// <returns>処理が成功したかどうか</returns>
    ValueTask<bool> RunProcessAsync(string arguments, int timeoutSeconds, string loggingProgramName = "", CancellationToken cancellationToken = default);
}
