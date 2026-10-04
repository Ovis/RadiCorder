namespace RadiCorder.Logics.Services;

/// <summary>
/// 引数を分割したままFFmpegへ渡す実装。文字列版は既存の連携用に保持する。
/// </summary>
public interface IFfmpegArgumentService : IFfmpegService
{
    ValueTask<bool> RunProcessAsync(IReadOnlyList<string> arguments, int timeoutSeconds,
        string loggingProgramName = "", CancellationToken cancellationToken = default);
}
