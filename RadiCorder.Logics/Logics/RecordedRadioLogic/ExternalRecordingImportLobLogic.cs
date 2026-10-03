using RadiCorder.Logics.Infrastructure.Import;
using static RadiCorder.Logics.Infrastructure.Import.ExternalImportDefaults;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Models.ExternalImport;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic;

/// <summary>
/// 外部音声ファイルの取込を担当するロジック
/// </summary>
public class ExternalRecordingImportLobLogic(
    ILogger<ExternalRecordingImportLobLogic> logger,
    IAppConfigurationService config,
    TagLobLogic tagLobLogic,
    RadioDbContext dbContext)
{
    private readonly ExternalImportTemplateParser _templateParser = new(logger, config);

    /// <summary>
    /// 候補をCSVとして出力する。
    /// </summary>
    public byte[] ExportCandidatesCsv(IReadOnlyList<ExternalImportCandidateEntry> candidates) =>
        ExternalImportCsv.ExportCandidatesCsv(candidates);

    /// <summary>
    /// CSVを読み込んで候補一覧を再構築する。
    /// </summary>
    public ValueTask<(bool IsSuccess, List<ExternalImportCandidateEntry> Candidates, List<string> Errors)> ImportCandidatesCsvAsync(
        Stream stream, CancellationToken cancellationToken = default) =>
        ExternalImportCsv.ImportCandidatesCsvAsync(stream, GetRootPath(), cancellationToken);

    /// <summary>
    /// 録音保存先をスキャンして未登録の取込候補を返す
    /// </summary>
    public async ValueTask<List<ExternalImportCandidateEntry>> ScanCandidatesAsync(bool applyDefaultTag = true, CancellationToken cancellationToken = default)
    {
        var rootPath = GetRootPath();
        var existingSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var relativePath in dbContext.RecordingFiles
                           .AsNoTracking()
                           .Select(x => x.FileRelativePath)
                           .AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            var normalized = ExternalImportPaths.NormalizeExistingToAbsolutePath(relativePath, rootPath);
            if (!string.IsNullOrEmpty(normalized))
            {
                existingSet.Add(normalized);
            }
        }

        var candidates = new List<ExternalImportCandidateEntry>();
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        foreach (var filePath in Directory.EnumerateFiles(rootPath, "*.*", enumerationOptions))
        {
            string ext;
            try
            {
                ext = Path.GetExtension(filePath);
                if (!AllowedExtensions.Contains(ext))
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.ZLogDebug(ex, $"外部取込スキャン中に拡張子取得に失敗したためスキップ: path={filePath}");
                continue;
            }

            string relativePath;
            try
            {
                relativePath = Path.GetRelativePath(rootPath, filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.ZLogDebug(ex, $"外部取込スキャン中に相対パス計算に失敗したためスキップ: path={filePath}");
                continue;
            }

            if (!ExternalImportPaths.TryResolveManagedFilePath(relativePath, rootPath, out var normalizedPath, out var normalizedRelativePath))
            {
                continue;
            }

            if (existingSet.Contains(normalizedPath))
            {
                continue;
            }

            string title;
            string fileName;
            DateTime lastWriteAt;
            try
            {
                title = ExternalImportAudioMetadata.ReadTitleMetadata(filePath);
                fileName = Path.GetFileNameWithoutExtension(filePath);
                lastWriteAt = File.GetLastWriteTimeUtc(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.ZLogDebug(ex, $"外部取込スキャン中にファイル情報取得に失敗したためスキップ: path={filePath}");
                continue;
            }

            if (lastWriteAt == DateTime.MinValue)
            {
                lastWriteAt = DateTime.UtcNow;
            }

            var stationName = DefaultStationName;
            var broadcastAt = ResolveFallbackBroadcastAt(lastWriteAt);
            var templateEnriched = _templateParser.TryEnrichFromTemplates(
                normalizedRelativePath,
                out var parsedStationName,
                out var parsedTitle,
                out var parsedBroadcastAt);
            if (templateEnriched)
            {
                if (!string.IsNullOrWhiteSpace(parsedStationName))
                {
                    stationName = parsedStationName.Trim();
                }

                // 音声タグ優先。未取得時のみテンプレート解析結果を採用。
                if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(parsedTitle))
                {
                    title = parsedTitle;
                }

                if (parsedBroadcastAt.HasValue)
                {
                    broadcastAt = parsedBroadcastAt.Value;
                }
            }
            else
            {
                logger.ZLogDebug(
                    $"外部取込テンプレート解析に失敗: file={normalizedRelativePath}, dirTemplate={config.RecordDirectoryRelativePath}, fileTemplate={config.RecordFileNameTemplate}");
            }

            if (string.Equals(stationName, DefaultStationName, StringComparison.Ordinal))
            {
                logger.ZLogDebug(
                    $"外部取込で放送局名を特定できませんでした: file={normalizedRelativePath}, title={title}, parsedStation={parsedStationName}, parsedTitle={parsedTitle}, parsedBroadcastAt={parsedBroadcastAt}");
            }

            candidates.Add(new ExternalImportCandidateEntry
            {
                IsSelected = true,
                FilePath = normalizedRelativePath,
                Title = string.IsNullOrWhiteSpace(title) ? fileName : title.Trim(),
                Description = string.Empty,
                StationName = stationName,
                BroadcastAt = broadcastAt,
                Tags = applyDefaultTag ? [DefaultTagName] : []
            });
        }

        return candidates
            .OrderByDescending(x => x.BroadcastAt)
            .ToList();
    }



    /// <summary>
    /// 候補を録音済み番組として保存する
    /// </summary>
    public async ValueTask<ExternalImportSaveResult> SaveCandidatesAsync(
        IReadOnlyList<ExternalImportCandidateEntry> candidates,
        bool markAsListened = false,
        CancellationToken cancellationToken = default)
    {
        var selected = candidates
            .Where(x => x.IsSelected)
            .ToList();
        var result = new ExternalImportSaveResult();
        if (selected.Count == 0)
        {
            result.Errors.Add(new ExternalImportValidationError
            {
                FilePath = string.Empty,
                Message = "取り込み対象が選択されていません。"
            });
            return result;
        }

        var rootPath = GetRootPath();
        var errors = await ValidateCandidatesAsync(selected, rootPath, cancellationToken);
        if (errors.Count > 0)
        {
            result.Errors = errors;
            return result;
        }

        var existingTags = await dbContext.RecordingTags
            .ToDictionaryAsync(x => x.NormalizedName, x => x, cancellationToken);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var candidate in selected)
            {
                var recordingId = Ulid.NewUlid();
                if (!ExternalImportPaths.TryResolveManagedFilePath(candidate.FilePath, rootPath, out var normalizedPath, out var relativePath))
                {
                    throw new InvalidOperationException("ファイルパスの正規化に失敗しました。");
                }
                var stationName = string.IsNullOrWhiteSpace(candidate.StationName) ? DefaultStationName : candidate.StationName.Trim();
                var stationId = BuildExternalStationId(stationName);
                var recordingDuration = ExternalImportAudioMetadata.ResolveRecordingDuration(normalizedPath);

                var recording = new Recording
                {
                    Id = recordingId,
                    ServiceKind = RadioServiceKind.Other,
                    ProgramId = $"EXT-{recordingId}",
                    StationId = stationId,
                    AreaId = string.Empty,
                    StartDateTime = candidate.BroadcastAt.ToUniversalTime(),
                    EndDateTime = candidate.BroadcastAt.ToUniversalTime().Add(recordingDuration),
                    IsTimeFree = false,
                    State = RecordingState.Completed,
                    ErrorMessage = null,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    SourceType = RecordingSourceType.ExternalImport,
                    IsListened = markAsListened
                };

                var metadata = new RecordingMetadata
                {
                    RecordingId = recordingId,
                    StationName = stationName,
                    Title = candidate.Title.Trim(),
                    Subtitle = string.Empty,
                    Performer = string.Empty,
                    Description = candidate.Description.Trim(),
                    ProgramUrl = string.Empty
                };

                var file = new RecordingFile
                {
                    RecordingId = recordingId,
                    FileRelativePath = relativePath,
                    HasHlsFile = false,
                    HlsDirectoryPath = null
                };

                await dbContext.Recordings.AddAsync(recording, cancellationToken);
                await dbContext.RecordingMetadatas.AddAsync(metadata, cancellationToken);
                await dbContext.RecordingFiles.AddAsync(file, cancellationToken);

                var tagIds = new List<Guid>();
                foreach (var tagName in candidate.Tags
                             .Select(x => x.Trim())
                             .Where(x => !string.IsNullOrWhiteSpace(x))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var normalizedName = tagName.ToLowerInvariant();
                    if (normalizedName == DefaultTagNormalizedName)
                    {
                        var defaultTag = await EnsureDefaultTagAsync(existingTags, cancellationToken);
                        tagIds.Add(defaultTag.Id);
                        continue;
                    }

                    if (existingTags.TryGetValue(normalizedName, out var tag))
                    {
                        tag.LastUsedAt = DateTimeOffset.UtcNow;
                        tag.UpdatedAt = DateTimeOffset.UtcNow;
                        tagIds.Add(tag.Id);
                    }
                }

                foreach (var tagId in tagIds.Distinct())
                {
                    await dbContext.RecordingTagRelations.AddAsync(new RecordingTagRelation
                    {
                        RecordingId = recordingId,
                        TagId = tagId
                    }, cancellationToken);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            result.SavedCount = selected.Count;
            return result;
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"外部取込保存に失敗しました。");
            await transaction.RollbackAsync(cancellationToken);
            result.Errors.Add(new ExternalImportValidationError
            {
                FilePath = string.Empty,
                Message = "保存処理に失敗しました。"
            });
            return result;
        }
    }

    private async ValueTask<RecordingTag> EnsureDefaultTagAsync(
        IDictionary<string, RecordingTag> existingTags,
        CancellationToken cancellationToken)
    {
        if (existingTags.TryGetValue(DefaultTagNormalizedName, out var existing))
        {
            existing.LastUsedAt = DateTimeOffset.UtcNow;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            return existing;
        }

        var created = await tagLobLogic.CreateTagAsync(DefaultTagName, cancellationToken);
        var tag = await dbContext.RecordingTags.SingleAsync(x => x.Id == created.Id, cancellationToken);
        existingTags[DefaultTagNormalizedName] = tag;
        return tag;
    }

    private async ValueTask<List<ExternalImportValidationError>> ValidateCandidatesAsync(
        IReadOnlyList<ExternalImportCandidateEntry> candidates,
        string rootPath,
        CancellationToken cancellationToken)
    {
        var errors = new List<ExternalImportValidationError>();
        var duplicated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingFilePaths = await dbContext.RecordingFiles
            .AsNoTracking()
            .Select(x => x.FileRelativePath)
            .ToListAsync(cancellationToken);
        var existingSet = existingFilePaths
            .Select(x => ExternalImportPaths.NormalizeExistingToAbsolutePath(x, rootPath))
            .Where(x => !string.IsNullOrEmpty(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTagNames = await dbContext.RecordingTags
            .AsNoTracking()
            .Select(x => x.NormalizedName)
            .ToHashSetAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (!ExternalImportPaths.TryResolveManagedFilePath(candidate.FilePath, rootPath, out var normalizedPath, out _))
            {
                errors.Add(NewError(candidate.FilePath, "ファイルパスが不正です。"));
                continue;
            }

            if (!duplicated.Add(normalizedPath))
            {
                errors.Add(NewError(candidate.FilePath, "CSV内で同じファイルパスが重複しています。"));
            }

            if (!File.Exists(normalizedPath))
            {
                errors.Add(NewError(candidate.FilePath, "ファイルが存在しません。"));
            }

            if (!AllowedExtensions.Contains(Path.GetExtension(normalizedPath)))
            {
                errors.Add(NewError(candidate.FilePath, "対応していない拡張子です。"));
            }

            if (existingSet.Contains(normalizedPath))
            {
                errors.Add(NewError(candidate.FilePath, "すでに録音済み番組として登録されています。"));
            }

            if (string.IsNullOrWhiteSpace(candidate.Title))
            {
                errors.Add(NewError(candidate.FilePath, "タイトルは必須です。"));
            }

            if ((candidate.Title?.Length ?? 0) > 100)
            {
                errors.Add(NewError(candidate.FilePath, "タイトルは100文字以内で入力してください。"));
            }

            if ((candidate.Description?.Length ?? 0) > 250)
            {
                errors.Add(NewError(candidate.FilePath, "説明は250文字以内で入力してください。"));
            }

            if ((candidate.StationName?.Length ?? 0) > 150)
            {
                errors.Add(NewError(candidate.FilePath, "放送局名は150文字以内で入力してください。"));
            }

            var unknownTags = candidate.Tags
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => new { Display = x, Normalized = x.ToLowerInvariant() })
                .Where(x => x.Normalized != DefaultTagNormalizedName)
                .Where(x => !existingTagNames.Contains(x.Normalized))
                .Select(x => x.Display)
                .ToList();
            if (unknownTags.Count > 0)
            {
                errors.Add(NewError(candidate.FilePath, $"未登録タグが含まれています: {string.Join(", ", unknownTags)}"));
            }
        }

        return errors;
    }

    private static ExternalImportValidationError NewError(string path, string message)
    {
        return new ExternalImportValidationError
        {
            FilePath = path,
            Message = message
        };
    }

    private static string BuildExternalStationId(string stationName)
    {
        var normalized = stationName.Trim().ToLowerInvariant();
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(bytes)[..16];
        return $"EXT-{hash}";
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

    private DateTimeOffset ResolveFallbackBroadcastAt(DateTime utcDateTime)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(config.ExternalImportFileTimeZoneId);
            var dateTimeOffset = new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);
            return TimeZoneInfo.ConvertTime(dateTimeOffset, tz);
        }
        catch
        {
            var dateTimeOffset = new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), TimeSpan.Zero);
            return TimeZoneInfo.ConvertTime(dateTimeOffset, JapanTimeZone.Resolve());
        }
    }







}
