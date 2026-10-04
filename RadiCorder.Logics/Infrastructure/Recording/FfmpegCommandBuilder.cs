namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// 固定オプションと外部データを分け、外部データを一つの引数として保持する。
/// </summary>
internal sealed class FfmpegCommandBuilder
{
    private readonly List<string> _arguments = [];
    public void Append(string constantOptions) => _arguments.AddRange(constantOptions.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    public void Add(params string[] arguments) => _arguments.AddRange(arguments);
    public IReadOnlyList<string> Build() => _arguments;
}
