using System.Text;

namespace RadiCorder.Logics.Services;

public static class FfmpegArgumentExtensions
{
    public static ValueTask<bool> RunArgumentsAsync(this IFfmpegService service, IReadOnlyList<string> arguments,
        int timeoutSeconds, string loggingProgramName = "", CancellationToken cancellationToken = default)
        => service is IFfmpegArgumentService structured
            ? structured.RunProcessAsync(arguments, timeoutSeconds, loggingProgramName, cancellationToken)
            : service.RunProcessAsync(string.Join(' ', arguments.Select(QuoteArgument)), timeoutSeconds, loggingProgramName, cancellationToken);

    // 文字列版だけを実装する既存の連携でも、引用符と末尾のバックスラッシュを保持する。
    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var c in value)
        {
            if (c == '\\') { slashes++; continue; }
            result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            result.Append(c);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }
}
