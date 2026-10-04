using System.Text.Json;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// ファイル移動とDB確定の間で停止しても、実際の保存先を失わないよう記録する。
/// </summary>
public class RecordingFinalizationJournal(IAppConfigurationService config)
{
    public sealed record Entry(Ulid RecordingId, string? ScheduleJobId, MediaPath Path);
    private string DirectoryPath => System.IO.Path.Combine(config.TemporaryFileSaveDir, "recording-finalization");
    private string GetPath(Ulid id) => System.IO.Path.Combine(DirectoryPath, $"{id}.json");

    public void Write(Entry entry)
    {
        Directory.CreateDirectory(DirectoryPath);
        var target = GetPath(entry.RecordingId);
        var temporary = target + ".tmp";
        // ファイル移動前にflushし、不完全なJSONが復旧対象として見えないよう置換する。
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, entry);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, target, overwrite: true);
    }

    public IEnumerable<string> GetPendingFiles() => Directory.Exists(DirectoryPath)
        ? Directory.GetFiles(DirectoryPath, "*.json") : [];

    public Entry Read(string filePath)
    {
        var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(filePath))
            ?? throw new InvalidDataException("録音確定の復旧情報が空です。");
        var expectedPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(config.RecordFileSaveDir, entry.Path.RelativePath));
        var root = System.IO.Path.GetFullPath(config.RecordFileSaveDir).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!expectedPath.StartsWith(root, comparison) || !expectedPath.Equals(System.IO.Path.GetFullPath(entry.Path.FinalFilePath), comparison) ||
            !System.IO.Path.GetFileName(filePath).Equals($"{entry.RecordingId}.json", StringComparison.Ordinal))
        {
            throw new InvalidDataException("録音確定の復旧情報のパスが不正です。");
        }
        return entry;
    }

    public void Complete(Ulid id) => File.Delete(GetPath(id));
}
