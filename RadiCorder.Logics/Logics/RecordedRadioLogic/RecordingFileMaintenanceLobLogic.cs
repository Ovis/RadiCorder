using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Models.ExternalImport;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic;

/// <summary>
/// 録音ファイルとDBの整合性メンテナンスを担当するロジック
/// </summary>
public class RecordingFileMaintenanceLobLogic(
    ILogger<RecordingFileMaintenanceLobLogic> logger,
    IAppConfigurationService config,
    RadioDbContext dbContext)
{
    /// <summary>
    /// 欠損ファイルレコードを抽出する
    /// </summary>
    public async ValueTask<RecordingFileMaintenanceScanResult> ScanMissingRecordsAsync(CancellationToken cancellationToken = default)
    {
        var rootPath = GetRootPath();
        var records = await LoadRecordEntriesAsync(cancellationToken);
        var fileIndex = BuildFileIndex(rootPath);
        var usedPaths = records
            .Where(entry => File.Exists(entry.ResolvedFullPath))
            .Select(entry => entry.ResolvedFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingEntries = records
            .Where(entry => !File.Exists(entry.ResolvedFullPath))
            .Select(entry =>
            {
                fileIndex.TryGetValue(entry.FileName, out var candidates);
                var candidatePaths = (candidates ?? [])
                    .Where(path => !usedPaths.Contains(path))
                    .ToList();
                return new RecordingFileMaintenanceEntry
                {
                    IssueType = "missing_file",
                    RecordingId = entry.RecordingId.ToString(),
                    Title = entry.Title,
                    StationName = entry.StationName,
                    StoredPath = entry.StoredPath,
                    FileName = entry.FileName,
                    CandidateCount = candidatePaths.Count,
                    CandidateRelativePaths = candidatePaths
                        .Take(5)
                        .Select(path => Path.GetRelativePath(rootPath, path))
                        .ToList()
                };
            })
            .ToList();

        var recoverableFailedEntries = records
            .Where(entry => entry.State == RecordingState.Failed)
            .Where(entry => File.Exists(entry.ResolvedFullPath))
            .Select(entry => new RecordingFileMaintenanceEntry
            {
                IssueType = "failed_with_existing_file",
                RecordingId = entry.RecordingId.ToString(),
                Title = entry.Title,
                StationName = entry.StationName,
                StoredPath = entry.StoredPath,
                FileName = entry.FileName,
                CandidateCount = 1,
                CandidateRelativePaths =
                [
                    Path.GetRelativePath(rootPath, entry.ResolvedFullPath)
                ]
            })
            .ToList();

        var entries = missingEntries
            .Concat(recoverableFailedEntries)
            .OrderBy(x => x.IssueType)
            .ThenBy(x => x.StationName)
            .ThenBy(x => x.Title)
            .ToList();

        return new RecordingFileMaintenanceScanResult
        {
            MissingCount = missingEntries.Count,
            RecoverableFailedCount = recoverableFailedEntries.Count,
            Entries = entries
        };
    }

    /// <summary>
    /// 欠損ファイルレコードを同名ファイルへ再紐付けする
    /// </summary>
    public async ValueTask<RecordingFileMaintenanceActionResult> RelinkMissingRecordsAsync(
        IReadOnlyCollection<Ulid>? targetIds = null,
        CancellationToken cancellationToken = default)
    {
        var rootPath = GetRootPath();
        var records = await LoadRecordEntriesAsync(cancellationToken);
        var fileIndex = BuildFileIndex(rootPath);
        var usedPaths = records
            .Where(x => File.Exists(x.ResolvedFullPath))
            .Select(x => x.ResolvedFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetIdSet = targetIds?.ToHashSet() ?? [];

        var targetRecords = records
            .Where(entry => !File.Exists(entry.ResolvedFullPath))
            .Concat(records.Where(entry => entry.State == RecordingState.Failed && File.Exists(entry.ResolvedFullPath)))
            .Where(entry => targetIdSet.Count == 0 || targetIdSet.Contains(entry.RecordingId))
            .DistinctBy(entry => entry.RecordingId)
            .ToList();

        var result = new RecordingFileMaintenanceActionResult
        {
            TargetCount = targetRecords.Count
        };

        foreach (var target in targetRecords)
        {
            try
            {
                // ファイルが存在し、状態のみFailedの場合はCompletedへ復旧する
                if (target.State == RecordingState.Failed && File.Exists(target.ResolvedFullPath))
                {
                    var failedRecord = await dbContext.Recordings.FindAsync([target.RecordingId], cancellationToken);
                    if (failedRecord == null)
                    {
                        result.FailCount++;
                        result.Details.Add(CreateDetail(target.RecordingId, "fail", "対象レコードが見つかりません。"));
                        continue;
                    }

                    failedRecord.State = RecordingState.Completed;
                    failedRecord.ErrorMessage = null;
                    failedRecord.UpdatedAt = DateTimeOffset.UtcNow;

                    var failedFile = await dbContext.RecordingFiles.FindAsync([target.RecordingId], cancellationToken);
                    if (failedFile != null)
                    {
                        failedFile.HasHlsFile = false;
                        failedFile.HlsDirectoryPath = null;
                    }

                    result.SuccessCount++;
                    result.Details.Add(CreateDetail(target.RecordingId, "success", "Failed状態をCompletedへ復旧しました。"));
                    continue;
                }

                if (!fileIndex.TryGetValue(target.FileName, out var candidates) || candidates.Count == 0)
                {
                    result.SkipCount++;
                    result.Details.Add(CreateDetail(target.RecordingId, "skip", "同名ファイルが見つかりません。"));
                    continue;
                }

                var available = candidates
                    .Where(path => !usedPaths.Contains(path) && !reservedPaths.Contains(path))
                    .ToList();

                if (available.Count != 1)
                {
                    result.SkipCount++;
                    result.Details.Add(CreateDetail(target.RecordingId, "skip", available.Count == 0
                        ? "同名ファイルはありますが、他レコードで使用済みです。"
                        : "同名ファイルが複数見つかりました。"));
                    continue;
                }

                var newPath = available[0];
                var relativePath = Path.GetRelativePath(rootPath, newPath);

                var file = await dbContext.RecordingFiles.FindAsync([target.RecordingId], cancellationToken);
                if (file == null)
                {
                    result.FailCount++;
                    result.Details.Add(CreateDetail(target.RecordingId, "fail", "対象のファイル情報が見つかりません。"));
                    continue;
                }

                file.FileRelativePath = relativePath;
                file.HasHlsFile = false;
                file.HlsDirectoryPath = null;

                usedPaths.Add(newPath);
                reservedPaths.Add(newPath);
                result.SuccessCount++;
                result.Details.Add(CreateDetail(target.RecordingId, "success", $"再紐付けしました。({relativePath})"));
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"再紐付け処理に失敗しました。");
                result.FailCount++;
                result.Details.Add(CreateDetail(target.RecordingId, "fail", "再紐付け処理に失敗しました。"));
            }
        }

        if (result.SuccessCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// 欠損ファイルのレコードをDBから削除する
    /// </summary>
    public async ValueTask<RecordingFileMaintenanceActionResult> DeleteMissingRecordsAsync(
        IReadOnlyCollection<Ulid>? targetIds = null,
        CancellationToken cancellationToken = default)
    {
        var records = await LoadRecordEntriesAsync(cancellationToken);
        var targetIdSet = targetIds?.ToHashSet() ?? [];
        var missingTargets = records
            .Where(entry => !File.Exists(entry.ResolvedFullPath))
            .Where(entry => targetIdSet.Count == 0 || targetIdSet.Contains(entry.RecordingId))
            .ToList();

        var result = new RecordingFileMaintenanceActionResult
        {
            TargetCount = missingTargets.Count
        };

        foreach (var target in missingTargets)
        {
            try
            {
                var record = await dbContext.Recordings.FindAsync([target.RecordingId], cancellationToken);
                if (record == null)
                {
                    result.SkipCount++;
                    result.Details.Add(CreateDetail(target.RecordingId, "skip", "対象レコードは既に削除されています。"));
                    continue;
                }

                dbContext.Recordings.Remove(record);
                result.SuccessCount++;
                result.Details.Add(CreateDetail(target.RecordingId, "success", "欠損レコードを削除しました。"));
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"欠損レコード削除に失敗しました。");
                result.FailCount++;
                result.Details.Add(CreateDetail(target.RecordingId, "fail", "欠損レコード削除に失敗しました。"));
            }
        }

        if (result.SuccessCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private static RecordingFileMaintenanceActionDetail CreateDetail(Ulid recordingId, string status, string message)
    {
        return new RecordingFileMaintenanceActionDetail
        {
            RecordingId = recordingId.ToString(),
            Status = status,
            Message = message
        };
    }

    private string GetRootPath()
    {
        var root = Path.GetFullPath(config.RecordFileSaveDir);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"録音保存先が存在しません。 path={root}");
        }
        return root;
    }

    private async ValueTask<List<RecordEntry>> LoadRecordEntriesAsync(CancellationToken cancellationToken)
    {
        var rootPath = GetRootPath();
        return await dbContext.RecordingFiles
            .AsNoTracking()
            .Join(dbContext.Recordings.AsNoTracking(),
                file => file.RecordingId,
                recording => recording.Id,
                (file, recording) => new { file, recording })
            .Join(dbContext.RecordingMetadatas.AsNoTracking(),
                pair => pair.file.RecordingId,
                meta => meta.RecordingId,
                (pair, meta) => new { pair.file, pair.recording, meta })
            .Select(x => new RecordEntry
            {
                RecordingId = x.file.RecordingId,
                State = x.recording.State,
                Title = x.meta.Title,
                StationName = x.meta.StationName,
                StoredPath = x.file.FileRelativePath,
                FileName = Path.GetFileName(x.file.FileRelativePath),
                ResolvedFullPath = ResolveToFullPath(rootPath, x.file.FileRelativePath)
            })
            .ToListAsync(cancellationToken);
    }

    private static Dictionary<string, List<string>> BuildFileIndex(string rootPath)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            if (!index.TryGetValue(fileName, out var values))
            {
                values = [];
                index[fileName] = values;
            }

            values.Add(path);
        }

        return index;
    }

    private static string ResolveToFullPath(string rootPath, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(rootPath, relativePath));
    }

    private sealed class RecordEntry
    {
        /// <summary>
        /// 録音ID
        /// </summary>
        public Ulid RecordingId { get; set; }

        /// <summary>
        /// 録音状態
        /// </summary>
        public RecordingState State { get; set; }

        /// <summary>
        /// タイトル
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 放送局名
        /// </summary>
        public string StationName { get; set; } = string.Empty;

        /// <summary>
        /// 保存パス
        /// </summary>
        public string StoredPath { get; set; } = string.Empty;

        /// <summary>
        /// ファイル名
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// 絶対パス
        /// </summary>
        public string ResolvedFullPath { get; set; } = string.Empty;
    }
}
