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
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".m4a",
        ".aac",
        ".flac",
        ".wav",
        ".ogg",
        ".opus",
        ".wma"
    };

    private const string DefaultStationName = "不明";
    private const string DefaultTagName = "外部取込";
    private static readonly string DefaultTagNormalizedName = DefaultTagName.ToLowerInvariant();
    private const int CsvMaxRows = 5000;
    private static readonly string[] ParseTokens =
    [
        "$StationName$",
        "$Title$",
        "$SYYYY$",
        "$SYY$",
        "$SMM$",
        "$SM$",
        "$SDD$",
        "$SD$",
        "$STHH$",
        "$STH$",
        "$STMM$",
        "$STM$",
        "$STSS$",
        "$STS$"
    ];

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
            var normalized = NormalizeExistingToAbsolutePath(relativePath, rootPath);
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

            if (!TryResolveManagedFilePath(relativePath, rootPath, out var normalizedPath, out var normalizedRelativePath))
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
                title = ReadTitleMetadata(filePath);
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
            var templateEnriched = TryEnrichFromTemplates(
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
    /// 候補をCSVとして出力する
    /// </summary>
    public byte[] ExportCandidatesCsv(IReadOnlyList<ExternalImportCandidateEntry> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"FilePath\",\"Title\",\"Description\",\"StationName\",\"BroadcastAt\",\"Tags\"");

        foreach (var candidate in candidates)
        {
            var tags = string.Join("|", candidate.Tags.Select(t => EscapeCsvFormula(t.Trim())));
            var line = string.Join(",",
                CsvEscape(candidate.FilePath),
                CsvEscape(EscapeCsvFormula(candidate.Title)),
                CsvEscape(EscapeCsvFormula(candidate.Description)),
                CsvEscape(EscapeCsvFormula(candidate.StationName)),
                CsvEscape(candidate.BroadcastAt.ToString("o", CultureInfo.InvariantCulture)),
                CsvEscape(tags));
            sb.AppendLine(line);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>
    /// CSVを読み込んで候補一覧を再構築する
    /// </summary>
    public ValueTask<(bool IsSuccess, List<ExternalImportCandidateEntry> Candidates, List<string> Errors)> ImportCandidatesCsvAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var rootPath = GetRootPath();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        };
        using var csv = new CsvReader(reader, csvConfig);

        var errors = new List<string>();
        var candidates = new List<ExternalImportCandidateEntry>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expected = new[] { "FilePath", "Title", "Description", "StationName", "BroadcastAt", "Tags" };

        try
        {
            if (!csv.Read())
            {
                return ValueTask.FromResult((false, candidates, new List<string> { "CSVが空です。" }));
            }

            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? Array.Empty<string>();
            if (headers.Length != expected.Length || headers.Where((value, index) => !value.Equals(expected[index], StringComparison.Ordinal)).Any())
            {
                return ValueTask.FromResult((false, candidates, new List<string> { "CSVヘッダーが不正です。" }));
            }
        }
        catch
        {
            return ValueTask.FromResult((false, candidates, new List<string> { "CSVヘッダーが不正です。" }));
        }

        try
        {
            while (csv.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (candidates.Count >= CsvMaxRows)
                {
                    errors.Add($"CSV行数が上限を超えています。上限: {CsvMaxRows} 行");
                    return ValueTask.FromResult((false, candidates, errors));
                }

                var rowNumber = csv.Parser.Row;
                string filePath;
                string title;
                string description;
                string stationName;
                string broadcastAtText;
                string tagsText;
                try
                {
                    filePath = csv.GetField("FilePath")?.Trim() ?? string.Empty;
                    title = csv.GetField("Title")?.Trim() ?? string.Empty;
                    description = csv.GetField("Description")?.Trim() ?? string.Empty;
                    stationName = csv.GetField("StationName")?.Trim() ?? string.Empty;
                    broadcastAtText = csv.GetField("BroadcastAt")?.Trim() ?? string.Empty;
                    tagsText = csv.GetField("Tags")?.Trim() ?? string.Empty;
                }
                catch
                {
                    errors.Add($"{rowNumber}行目: 列数が不正です。");
                    continue;
                }

                if (!DateTimeOffset.TryParse(broadcastAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var broadcastAt))
                {
                    errors.Add($"{rowNumber}行目: 放送日時の形式が不正です。");
                    continue;
                }

                if (!TryResolveManagedFilePath(filePath, rootPath, out var normalizedPath, out var normalizedRelativePath))
                {
                    errors.Add($"{rowNumber}行目: ファイルパスが不正です。");
                    continue;
                }

                if (!seenPaths.Add(normalizedPath))
                {
                    errors.Add($"{rowNumber}行目: 同じファイルパスが重複しています。");
                    continue;
                }

                var ext = Path.GetExtension(normalizedPath);
                if (!AllowedExtensions.Contains(ext))
                {
                    errors.Add($"{rowNumber}行目: 対応していない拡張子です。");
                    continue;
                }

                if (!File.Exists(normalizedPath))
                {
                    errors.Add($"{rowNumber}行目: ファイルが存在しません。");
                    continue;
                }

                var tags = tagsText
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (tags.Count == 0)
                {
                    tags.Add(DefaultTagName);
                }

                candidates.Add(new ExternalImportCandidateEntry
                {
                    IsSelected = true,
                    FilePath = normalizedRelativePath,
                    Title = title,
                    Description = description,
                    StationName = string.IsNullOrWhiteSpace(stationName) ? DefaultStationName : stationName,
                    BroadcastAt = broadcastAt,
                    Tags = tags
                });
            }
        }
        catch (CsvHelperException)
        {
            errors.Add("CSVの解析に失敗しました。形式を確認してください。");
        }

        return ValueTask.FromResult((errors.Count == 0, candidates, errors));
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
                if (!TryResolveManagedFilePath(candidate.FilePath, rootPath, out var normalizedPath, out var relativePath))
                {
                    throw new InvalidOperationException("ファイルパスの正規化に失敗しました。");
                }
                var stationName = string.IsNullOrWhiteSpace(candidate.StationName) ? DefaultStationName : candidate.StationName.Trim();
                var stationId = BuildExternalStationId(stationName);
                var recordingDuration = ResolveRecordingDuration(normalizedPath);

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
            .Select(x => NormalizeExistingToAbsolutePath(x, rootPath))
            .Where(x => !string.IsNullOrEmpty(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTagNames = await dbContext.RecordingTags
            .AsNoTracking()
            .Select(x => x.NormalizedName)
            .ToHashSetAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (!TryResolveManagedFilePath(candidate.FilePath, rootPath, out var normalizedPath, out _))
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

    private static string ReadTitleMetadata(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            return tagFile.Tag.Title ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static TimeSpan ResolveRecordingDuration(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var duration = tagFile.Properties.Duration;
            if (duration > TimeSpan.Zero)
            {
                var roundedSeconds = Math.Max(1, Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
                return TimeSpan.FromSeconds(roundedSeconds);
            }
        }
        catch
        {
            // 読み取り不能なファイルは最小値へフォールバック
        }

        return TimeSpan.FromSeconds(1);
    }

    private bool TryEnrichFromTemplates(
        string relativePath,
        out string stationName,
        out string title,
        out DateTimeOffset? broadcastAt)
    {
        stationName = string.Empty;
        title = string.Empty;
        broadcastAt = null;

        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hasDirectoryTemplate = false;
        var hasFileTemplate = false;
        var matchedDirectory = false;
        var matchedFile = false;

        var directoryTemplate = NormalizePathForTemplate(config.RecordDirectoryRelativePath ?? string.Empty).Trim('/');
        var fileTemplate = (config.RecordFileNameTemplate ?? string.Empty).Trim();

        var normalizedRelativePath = NormalizePathForTemplate(relativePath).Trim('/');
        var lastSeparatorIndex = normalizedRelativePath.LastIndexOf('/');
        var relativeDirectory = lastSeparatorIndex >= 0
            ? normalizedRelativePath[..lastSeparatorIndex]
            : string.Empty;
        var relativeFileNameWithExtension = lastSeparatorIndex >= 0
            ? normalizedRelativePath[(lastSeparatorIndex + 1)..]
            : normalizedRelativePath;
        var relativeFileName = Path.GetFileNameWithoutExtension(relativeFileNameWithExtension);

        if (!string.IsNullOrEmpty(directoryTemplate))
        {
            hasDirectoryTemplate = true;
            matchedDirectory = TryMatchTemplate(directoryTemplate, relativeDirectory, captured);

            // テンプレート全体が一致しない場合でも、ディレクトリ階層が部分的に一致する箇所から
            // 放送局名/タイトルの取りこぼしを防ぐ。
            TryCaptureDirectoryTokensBySegment(directoryTemplate, relativeDirectory, captured);
        }

        if (!string.IsNullOrEmpty(fileTemplate))
        {
            hasFileTemplate = true;
            matchedFile = TryMatchTemplate(fileTemplate, relativeFileName, captured);
        }

        if (!hasDirectoryTemplate && !hasFileTemplate)
        {
            logger.ZLogDebug($"外部取込テンプレートが未設定のため補完をスキップ: relativePath={relativePath}");
            return false;
        }

        if (!matchedDirectory && !matchedFile)
        {
            // 先頭セグメント補完などで token を回収できている場合は、
            // テンプレート完全一致でなくても補完結果を採用する。
            if (captured.Count > 0)
            {
                logger.ZLogDebug(
                    $"外部取込テンプレート部分一致で補完を継続: relativePath={relativePath}, captured={FormatCaptured(captured)}");
            }
            else
            {
                logger.ZLogDebug(
                    $"外部取込テンプレート不一致: relativePath={relativePath}, relativeDirectory={relativeDirectory}, relativeFileName={relativeFileName}, dirTemplate={directoryTemplate}, fileTemplate={fileTemplate}, captured={FormatCaptured(captured)}");
                return false;
            }
        }

        if (captured.TryGetValue("StationName", out var parsedStationName))
        {
            stationName = parsedStationName;
        }

        if (captured.TryGetValue("Title", out var parsedTitle))
        {
            title = parsedTitle;
        }

        broadcastAt = TryBuildBroadcastAt(captured);
        if (!broadcastAt.HasValue && TryParseBroadcastAtFromFileName(relativeFileName, out var parsedBroadcastAt, out var parsedTitleFromFileName))
        {
            broadcastAt = parsedBroadcastAt;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = parsedTitleFromFileName;
            }
        }

        logger.ZLogDebug(
            $"外部取込テンプレート解析結果: relativePath={relativePath}, matchedDirectory={matchedDirectory}, matchedFile={matchedFile}, station={stationName}, title={title}, broadcastAt={broadcastAt}, captured={FormatCaptured(captured)}");

        return true;
    }

    private static string FormatCaptured(IReadOnlyDictionary<string, string> captured)
    {
        if (captured.Count == 0)
        {
            return "{}";
        }

        return "{" + string.Join(", ", captured.Select(kv => $"{kv.Key}={kv.Value}")) + "}";
    }

    private static void TryCaptureDirectoryTokensBySegment(
        string directoryTemplate,
        string relativeDirectory,
        IDictionary<string, string> captured)
    {
        var templateSegments = directoryTemplate
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var directorySegments = relativeDirectory
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (templateSegments.Length == 0 || directorySegments.Length == 0)
        {
            return;
        }

        var segmentCount = Math.Min(templateSegments.Length, directorySegments.Length);
        for (var i = 0; i < segmentCount; i++)
        {
            var templateSegment = templateSegments[i];
            var directorySegment = directorySegments[i];
            if (string.IsNullOrWhiteSpace(directorySegment))
            {
                continue;
            }

            if (templateSegment.Equals("$StationName$", StringComparison.Ordinal) &&
                !captured.ContainsKey("StationName"))
            {
                captured["StationName"] = directorySegment;
                continue;
            }

            if (templateSegment.Equals("$Title$", StringComparison.Ordinal) &&
                !captured.ContainsKey("Title"))
            {
                captured["Title"] = directorySegment;
            }
        }
    }

    private static bool TryMatchTemplate(string template, string value, IDictionary<string, string> captured)
    {
        if (string.IsNullOrEmpty(template))
        {
            return true;
        }

        var patternBuilder = new StringBuilder("^");
        var keys = new List<string>();
        var index = 0;
        var groupIndex = 0;

        while (index < template.Length)
        {
            var nextToken = ParseTokens
                .Select(token => new { Token = token, Position = template.IndexOf(token, index, StringComparison.Ordinal) })
                .Where(x => x.Position >= 0)
                .OrderBy(x => x.Position)
                .FirstOrDefault();

            if (nextToken == null)
            {
                patternBuilder.Append(Regex.Escape(template[index..]));
                break;
            }

            if (nextToken.Position > index)
            {
                patternBuilder.Append(Regex.Escape(template[index..nextToken.Position]));
            }

            var key = nextToken.Token.Trim('$');
            keys.Add(key);
            groupIndex++;
            patternBuilder.Append($"(?<g{groupIndex}>{TokenRegex(key)})");
            index = nextToken.Position + nextToken.Token.Length;
        }

        patternBuilder.Append("$");
        var regex = new Regex(patternBuilder.ToString(), RegexOptions.CultureInvariant);
        var match = regex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        for (var i = 0; i < keys.Count; i++)
        {
            var groupValue = match.Groups[$"g{i + 1}"].Value;
            if (string.IsNullOrEmpty(groupValue))
            {
                continue;
            }

            if (!captured.ContainsKey(keys[i]))
            {
                captured[keys[i]] = groupValue;
            }
        }

        return true;
    }

    private static string TokenRegex(string key)
    {
        return key switch
        {
            "SYYYY" => @"\d{4}",
            "SYY" => @"\d{2}",
            "SMM" => @"\d{2}",
            "SM" => @"\d{1,2}",
            "SDD" => @"\d{2}",
            "SD" => @"\d{1,2}",
            "STHH" => @"\d{2}",
            "STH" => @"\d{1,2}",
            "STMM" => @"\d{2}",
            "STM" => @"\d{1,2}",
            "STSS" => @"\d{2}",
            "STS" => @"\d{1,2}",
            _ => @".+?"
        };
    }

    private static DateTimeOffset? TryBuildBroadcastAt(IReadOnlyDictionary<string, string> captured)
    {
        if (!TryReadNumber(captured, "SYYYY", "SYY", out var year) ||
            !TryReadNumber(captured, "SMM", "SM", out var month) ||
            !TryReadNumber(captured, "SDD", "SD", out var day))
        {
            return null;
        }

        if (!TryReadNumber(captured, "STHH", "STH", out var hour))
        {
            hour = 0;
        }

        if (!TryReadNumber(captured, "STMM", "STM", out var minute))
        {
            minute = 0;
        }

        if (!TryReadNumber(captured, "STSS", "STS", out var second))
        {
            second = 0;
        }

        if (year < 1900 || month is < 1 or > 12 || day is < 1 or > 31 ||
            hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromHours(9));
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadNumber(IReadOnlyDictionary<string, string> captured, string primaryKey, string secondaryKey, out int value)
    {
        value = 0;
        if (captured.TryGetValue(primaryKey, out var primary) && int.TryParse(primary, out value))
        {
            return true;
        }

        if (captured.TryGetValue(secondaryKey, out var secondary) && int.TryParse(secondary, out value))
        {
            if (secondaryKey == "SYY" && value is >= 0 and <= 99)
            {
                value += 2000;
            }
            return true;
        }

        return false;
    }

    private static bool TryParseBroadcastAtFromFileName(string fileNameWithoutExtension, out DateTimeOffset broadcastAt, out string title)
    {
        broadcastAt = default;
        title = string.Empty;

        var match = Regex.Match(fileNameWithoutExtension, @"^(?<dt>\d{14})_(?<title>.+)$", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        var dtText = match.Groups["dt"].Value;
        title = match.Groups["title"].Value;
        if (!DateTime.TryParseExact(
                dtText,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateTime))
        {
            return false;
        }

        broadcastAt = new DateTimeOffset(dateTime, TimeSpan.FromHours(9));
        return true;
    }

    private static string NormalizePathForTemplate(string path)
    {
        return path.Replace('\\', '/');
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

    private static bool TryResolveManagedFilePath(string relativePath, string rootPath, out string fullPath, out string normalizedRelativePath)
    {
        fullPath = string.Empty;
        normalizedRelativePath = string.Empty;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var invalidChars = Path.GetInvalidPathChars();
        if (relativePath.IndexOfAny(invalidChars) >= 0)
        {
            return false;
        }

        var rawRelativePath = relativePath
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(rawRelativePath))
        {
            return false;
        }

        // ディレクトリトラバーサルを明示的に拒否
        var pathSegments = rawRelativePath
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pathSegments.Any(segment => segment is "." or ".."))
        {
            return false;
        }

        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(root, rawRelativePath));
        var combinedNoEnd = combined.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var isUnderRoot = OperatingSystem.IsWindows()
            ? combinedNoEnd.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            : combinedNoEnd.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

        if (!isUnderRoot)
        {
            return false;
        }

        if (ContainsReparsePoint(root, combined))
        {
            return false;
        }

        fullPath = combined;
        normalizedRelativePath = Path.GetRelativePath(root, combined);
        return true;
    }

    private static bool ContainsReparsePoint(string rootPath, string fullPath)
    {
        try
        {
            if (HasReparsePoint(rootPath))
            {
                return true;
            }

            var relative = Path.GetRelativePath(rootPath, fullPath);
            if (string.IsNullOrWhiteSpace(relative) ||
                relative.Equals(".", StringComparison.Ordinal) ||
                relative.Equals("..", StringComparison.Ordinal))
            {
                return false;
            }

            var current = rootPath;
            var segments = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                if (HasReparsePoint(current))
                {
                    return true;
                }
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static string NormalizeExistingToAbsolutePath(string storedPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return string.Empty;
        }

        try
        {
            if (Path.IsPathRooted(storedPath))
            {
                return Path.GetFullPath(storedPath);
            }

            return Path.GetFullPath(Path.Combine(rootPath, storedPath));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CsvEscape(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string EscapeCsvFormula(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var first = value[0];
        if (first is '=' or '+' or '-' or '@')
        {
            return "'" + value;
        }

        return value;
    }

}
