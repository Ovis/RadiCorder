using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// 録音ファイルの保存先を解決し、移動・削除を行う実装
/// </summary>
public class MediaStorageService(
    IAppConfigurationService config,
    ILogger<MediaStorageService>? logger = null,
    RecordingFinalizationJournal? finalizationJournal = null) : IRecoverableMediaStorageService
{
    /// <summary>
    /// 保存先パスを準備する
    /// </summary>
    public ValueTask<MediaPath> PrepareAsync(
        ProgramRecordingInfo programInfo,
        RecordingOptions options,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = GenerateFileName(programInfo, options);

        // 同一番組の同時録音でも衝突しないよう、一時ファイル名は必ずユニークにする
        var recordingsWorkDir = TemporaryStoragePaths.GetRecordingsWorkDirectory(config.TemporaryFileSaveDir);
        Directory.CreateDirectory(recordingsWorkDir);
        var tempFileName = $"{programInfo.ProgramId.ToSafeName()}_{Ulid.NewUlid()}.m4a";
        var tempFilePath = Path.Combine(recordingsWorkDir, tempFileName);

        return ValueTask.FromResult(new MediaPath(
            TempFilePath: tempFilePath,
            FinalFilePath: fileInfo.FileFullPath,
            RelativePath: fileInfo.FileRelativePath));
    }

    /// <summary>
    /// 一時ファイルを最終保存先へ確定させる
    /// </summary>
    public ValueTask<MediaPath> CommitAsync(MediaPath path, CancellationToken cancellationToken = default)
        => CommitCoreAsync(path, null, cancellationToken);

    public ValueTask<MediaPath> CommitRecordingAsync(MediaPath path, Ulid recordingId, string? scheduleJobId, CancellationToken cancellationToken)
        => CommitCoreAsync(path, new RecordingFinalizationJournal.Entry(recordingId, scheduleJobId, path), cancellationToken);

    public void CompleteFinalization(Ulid recordingId)
        => (finalizationJournal ?? new RecordingFinalizationJournal(config)).Complete(recordingId);

    public void CompleteJobFinalization(Ulid scheduleJobId)
    {
        var journal = finalizationJournal ?? new RecordingFinalizationJournal(config);
        foreach (var file in journal.GetPendingFiles())
        {
            var entry = journal.Read(file);
            if (entry.ScheduleJobId == scheduleJobId.ToString()) journal.Complete(entry.RecordingId);
        }
    }

    private ValueTask<MediaPath> CommitCoreAsync(MediaPath path, RecordingFinalizationJournal.Entry? entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 保存先ディレクトリが無ければ作成
        var dir = Path.GetDirectoryName(path.FinalFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var finalFilePath = path.FinalFilePath;
        var relativePath = path.RelativePath;

        // 同名ファイルがある場合はリネームして保存
        if (File.Exists(path.FinalFilePath))
        {
            finalFilePath = AddDuplicateSuffix(path.FinalFilePath);

            var relativeDirectory = Path.GetDirectoryName(path.RelativePath) ?? string.Empty;
            var renamedFileName = Path.GetFileName(finalFilePath);
            relativePath = string.IsNullOrEmpty(relativeDirectory)
                ? renamedFileName
                : Path.Combine(relativeDirectory, renamedFileName);
        }
        var committedPath = path with
        {
            FinalFilePath = finalFilePath,
            RelativePath = relativePath
        };
        if (entry != null)
        {
            (finalizationJournal ?? new RecordingFinalizationJournal(config)).Write(entry with { Path = committedPath });
        }
        try
        {
            File.Move(path.TempFilePath, finalFilePath);
        }
        catch
        {
            if (entry != null) CompleteFinalization(entry.RecordingId);
            throw;
        }
        return ValueTask.FromResult(committedPath);
    }

    /// <summary>
    /// 一時ファイルを削除する
    /// </summary>
    public ValueTask CleanupTempAsync(MediaPath path, CancellationToken cancellationToken = default)
    {
        if (File.Exists(path.TempFilePath))
        {
            File.Delete(path.TempFilePath);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 最終保存に失敗した一時ファイルを退避保存する
    /// </summary>
    public async ValueTask<SaveFailedFallbackResult> SaveFailedAsync(
        MediaPath path,
        SaveFailedFallbackMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var saveFailedDir = TemporaryStoragePaths.GetSaveFailedDirectory(config.TemporaryFileSaveDir);
        Directory.CreateDirectory(saveFailedDir);

        var originalName = Path.GetFileNameWithoutExtension(path.FinalFilePath);
        if (string.IsNullOrWhiteSpace(originalName))
        {
            originalName = Path.GetFileNameWithoutExtension(path.TempFilePath);
        }

        if (string.IsNullOrWhiteSpace(originalName))
        {
            originalName = "recording";
        }

        var originalExtension = Path.GetExtension(path.FinalFilePath);
        if (string.IsNullOrWhiteSpace(originalExtension))
        {
            originalExtension = Path.GetExtension(path.TempFilePath);
        }

        if (string.IsNullOrWhiteSpace(originalExtension))
        {
            originalExtension = ".m4a";
        }

        var fallbackFileName = $"{originalName}_save_failed_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}_{Ulid.NewUlid()}{originalExtension}";
        var fallbackFilePath = Path.Combine(saveFailedDir, fallbackFileName);

        File.Move(path.TempFilePath, fallbackFilePath);

        string? metadataPath = null;
        try
        {
            metadataPath = $"{fallbackFilePath}.meta.json";
            var payload = new
            {
                metadata.RecordedAt,
                metadata.ProgramId,
                metadata.StationId,
                metadata.Title,
                metadata.OriginalDestinationPath,
                FallbackPath = fallbackFilePath,
                metadata.ErrorType,
                metadata.ErrorMessage,
                metadata.ExpectedTags
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.ZLogWarning(ex, $"退避保存メタ情報の出力に失敗しました。 fallbackPath={fallbackFilePath}");
            metadataPath = null;
        }

        return new SaveFailedFallbackResult(fallbackFilePath, metadataPath);
    }

    /// <summary>
    /// ファイル名をテンプレートから生成する
    /// </summary>
    private RadioFileInfo GenerateFileName(ProgramRecordingInfo programInfo, RecordingOptions options)
    {
        var (fullPath, relativePath) = GenerateSavePath(programInfo, options);

        if (TryResolveFileNameTemplate(
                options.OutputFileNameTemplateOverride,
                programInfo,
                out var overriddenTemplate,
                "自動予約ルール",
                shouldLogInvalid: true))
        {
            return new RadioFileInfo(overriddenTemplate, fullPath, relativePath);
        }

        if (TryResolveFileNameTemplate(
                config.RecordFileNameTemplate,
                programInfo,
                out var configuredTemplate,
                "アプリ全体設定",
                shouldLogInvalid: false))
        {
            return new RadioFileInfo(configuredTemplate, fullPath, relativePath);
        }

        return new RadioFileInfo($"{programInfo.StartTime.ToJapanDateTime():yyyyMMddHHmmss}_{programInfo.Title.ToSafeName()}", fullPath, relativePath);
    }

    /// <summary>
    /// 保存先ディレクトリをテンプレートから生成する
    /// </summary>
    private (string FullPath, string RelativePath) GenerateSavePath(ProgramRecordingInfo programInfo, RecordingOptions options)
    {
        if (!config.RecordFileSaveDir.IsValidAbsolutePath())
        {
            throw new DomainException("録音フォルダの設定が不正です。");
        }

        if (TryResolveDirectoryTemplate(
                options.OutputDirectoryRelativePathOverride,
                programInfo,
                out var overriddenTemplate,
                "自動予約ルール",
                shouldLogInvalid: true))
        {
            if (config.RecordFileSaveDir.TryCombinePaths(overriddenTemplate, out var path))
            {
                return (path, overriddenTemplate);
            }
        }

        if (TryResolveDirectoryTemplate(
                config.RecordDirectoryRelativePath,
                programInfo,
                out var configuredTemplate,
                "アプリ全体設定",
                shouldLogInvalid: false) &&
            config.RecordFileSaveDir.TryCombinePaths(configuredTemplate, out var configuredPath))
        {
            return (configuredPath, configuredTemplate);
        }

        return (config.RecordFileSaveDir, string.Empty);
    }

    private static string NormalizePathSeparators(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// テンプレート置換用の辞書を生成する
    /// </summary>
    private Dictionary<string, string> ReplaceTemplate(ProgramRecordingInfo programInfo) => new()
    {
        { "$StationId$", programInfo.StationId.ToSafeName() },
        { "$StationName$", programInfo.StationName.ToSafeName() },
        { "$Title$", programInfo.Title.ToSafeName() },
        { "$SYYYY$", programInfo.StartTime.ToJapanDateTime().ToString("yyyy", CultureInfo.InvariantCulture) },
        { "$SYY$", programInfo.StartTime.ToJapanDateTime().ToString("yy", CultureInfo.InvariantCulture) },
        { "$SMM$", programInfo.StartTime.ToJapanDateTime().ToString("MM", CultureInfo.InvariantCulture) },
        { "$SM$", programInfo.StartTime.ToJapanDateTime().Month.ToString() },
        { "$SDD$", programInfo.StartTime.ToJapanDateTime().ToString("dd", CultureInfo.InvariantCulture) },
        { "$SD$", programInfo.StartTime.ToJapanDateTime().Day.ToString() },
        { "$STHH$", programInfo.StartTime.ToJapanDateTime().ToString("HH", CultureInfo.InvariantCulture) },
        { "$STH$", programInfo.StartTime.ToJapanDateTime().Hour.ToString() },
        { "$STMM$", programInfo.StartTime.ToJapanDateTime().ToString("mm", CultureInfo.InvariantCulture) },
        { "$STM$", programInfo.StartTime.ToJapanDateTime().Minute.ToString() },
        { "$STSS$", programInfo.StartTime.ToJapanDateTime().ToString("ss", CultureInfo.InvariantCulture) },
        { "$STS$", programInfo.StartTime.ToJapanDateTime().Second.ToString() },
        { "$EYYYY$", programInfo.EndTime.ToJapanDateTime().ToString("yyyy", CultureInfo.InvariantCulture) },
        { "$EYY$", programInfo.EndTime.ToJapanDateTime().ToString("yy", CultureInfo.InvariantCulture) },
        { "$EMM$", programInfo.EndTime.ToJapanDateTime().ToString("MM", CultureInfo.InvariantCulture) },
        { "$EM$", programInfo.EndTime.ToJapanDateTime().Month.ToString() },
        { "$EDD$", programInfo.EndTime.ToJapanDateTime().ToString("dd", CultureInfo.InvariantCulture) },
        { "$ED$", programInfo.EndTime.ToJapanDateTime().Day.ToString() },
        { "$ETHH$", programInfo.EndTime.ToJapanDateTime().ToString("HH", CultureInfo.InvariantCulture) },
        { "$ETH$", programInfo.EndTime.ToJapanDateTime().Hour.ToString() },
        { "$ETMM$", programInfo.EndTime.ToJapanDateTime().ToString("mm", CultureInfo.InvariantCulture) },
        { "$ETM$", programInfo.EndTime.ToJapanDateTime().Minute.ToString() },
        { "$ETSS$", programInfo.EndTime.ToJapanDateTime().ToString("ss", CultureInfo.InvariantCulture) },
        { "$ETS$", programInfo.EndTime.ToJapanDateTime().Second.ToString() },
    };

    /// <summary>
    /// 重複ファイル名にサフィックスを付与する
    /// </summary>
    private static string AddDuplicateSuffix(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);

        var suffix = $"_duplicate_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Ulid.NewUlid()}";
        return Path.Combine(dir, name + suffix + ext);
    }

    private bool TryResolveFileNameTemplate(
        string? template,
        ProgramRecordingInfo programInfo,
        out string resolvedTemplate,
        string sourceLabel,
        bool shouldLogInvalid)
    {
        resolvedTemplate = string.Empty;

        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var candidate = ApplyTemplate(template, programInfo);
        if (!candidate.IsValidFileName())
        {
            if (shouldLogInvalid)
            {
                logger?.ZLogWarning($"{sourceLabel}のファイル名テンプレートが不正なため、アプリ全体設定へフォールバックします。 template={template}");
            }

            return false;
        }

        resolvedTemplate = candidate;
        return true;
    }

    private bool TryResolveDirectoryTemplate(
        string? template,
        ProgramRecordingInfo programInfo,
        out string resolvedTemplate,
        string sourceLabel,
        bool shouldLogInvalid)
    {
        resolvedTemplate = string.Empty;

        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var candidate = NormalizePathSeparators(ApplyTemplate(template, programInfo));

        if (Path.IsPathRooted(candidate))
        {
            candidate = candidate.TrimStart(Path.GetPathRoot(candidate)?.ToCharArray() ?? []);
        }

        if (!IsValidRelativeDirectoryTemplate(candidate))
        {
            if (shouldLogInvalid)
            {
                logger?.ZLogWarning($"{sourceLabel}の保存先テンプレートが不正なため、アプリ全体設定へフォールバックします。 template={template}");
            }

            return false;
        }

        resolvedTemplate = candidate;
        return true;
    }

    private string ApplyTemplate(string template, ProgramRecordingInfo programInfo)
    {
        var result = template;
        var replacements = ReplaceTemplate(programInfo);

        foreach (var placeholder in replacements)
        {
            result = result.Replace(placeholder.Key, placeholder.Value);
        }

        return result;
    }

    private static bool IsValidRelativeDirectoryTemplate(string path)
    {
        if (!path.IsValidRelativePath())
        {
            return false;
        }

        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var segments = path
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\', '/'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.All(segment => segment.IndexOfAny(invalidFileNameChars) < 0);
    }
}
