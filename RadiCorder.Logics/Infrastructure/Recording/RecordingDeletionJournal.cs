using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// 削除の途中で停止した場合、DBの存在に従ってファイルを復元または削除する。
/// </summary>
public class RecordingDeletionJournal(IAppConfigurationService config)
{
    public sealed record StagedFile(string Original, string Staged, bool IsDirectory);
    public sealed record Entry(Ulid RecordingId, List<StagedFile> Files);
    private string DirectoryPath => Path.Combine(config.TemporaryFileSaveDir, "recording-deletion");
    private string GetPath(Ulid id) => Path.Combine(DirectoryPath, $"{id}.json");

    public void Write(Entry entry)
    {
        Directory.CreateDirectory(DirectoryPath);
        var target = GetPath(entry.RecordingId);
        using (var stream = new FileStream(target + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, entry);
            stream.Flush(flushToDisk: true);
        }
        File.Move(target + ".tmp", target, overwrite: true);
    }

    public void Complete(Ulid id) => File.Delete(GetPath(id));

    public async ValueTask RecoverAsync(RadioDbContext db, ILogger logger, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(DirectoryPath)) return;
        foreach (var path in Directory.GetFiles(DirectoryPath, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path)) ?? throw new InvalidDataException("削除の復旧情報が空です。");
                if (Path.GetFileName(path) != $"{entry.RecordingId}.json") throw new InvalidDataException("削除の復旧IDが不正です。");
                foreach (var file in entry.Files) Validate(entry.RecordingId, file);
                var exists = await db.Recordings.AsNoTracking().AnyAsync(x => x.Id == entry.RecordingId, cancellationToken);
                foreach (var file in entry.Files.AsEnumerable().Reverse())
                {
                    if (file.IsDirectory ? !Directory.Exists(file.Staged) : !File.Exists(file.Staged)) continue;
                    if (exists)
                    {
                        // 復元先に別ファイルがあれば上書きせず、復旧情報を保持する。
                        if (file.IsDirectory) Directory.Move(file.Staged, file.Original);
                        else File.Move(file.Staged, file.Original);
                    }
                    else
                    {
                        if (file.IsDirectory) Directory.Delete(file.Staged, true);
                        else File.Delete(file.Staged);
                    }
                }
                Complete(entry.RecordingId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"録音削除の復旧に失敗しました。journal={path}");
            }
        }
    }

    private void Validate(Ulid id, StagedFile file)
    {
        var root = Path.GetFullPath(file.IsDirectory ? TemporaryStoragePaths.GetHlsCacheRootDirectory(config.TemporaryFileSaveDir) : config.RecordFileSaveDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!Path.GetFullPath(file.Original).StartsWith(root, comparison) || file.Staged != file.Original + $".delete-{id}")
            throw new InvalidDataException("録音削除の復旧パスが不正です。");
    }
}
