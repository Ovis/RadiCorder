using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// 録音結果を新スキーマに保存する実装
/// </summary>
public class RecordingRepository(
    ILogger<RecordingRepository> logger,
    RadioDbContext dbContext) : IRecordingRepository
{
    /// <summary>
    /// 録音レコードを作成する
    /// </summary>
    public async ValueTask<Ulid> CreateAsync(ProgramRecordingInfo programInfo, MediaPath path, RecordingOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path.RelativePath))
        {
            throw new InvalidOperationException("録音ファイルの相対パスが空です。");
        }

        var id = Ulid.NewUlid();

        // 録音状態
        var recording = new RdbContext.Recording
        {
            Id = id,
            ServiceKind = options.ServiceKind,
            ProgramId = programInfo.ProgramId,
            StationId = programInfo.StationId,
            AreaId = programInfo.AreaId,
            StartDateTime = programInfo.StartTime.ToUniversalTime(),
            EndDateTime = programInfo.EndTime.ToUniversalTime(),
            IsTimeFree = options.IsTimeFree,
            State = RecordingState.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            SourceType = RecordingSourceType.Recorded,
            IsListened = false
        };

        // 番組情報
        var recordMetadata = new RecordingMetadata
        {
            RecordingId = id,
            StationName = programInfo.StationName,
            Title = programInfo.Title,
            Subtitle = programInfo.Subtitle,
            Performer = programInfo.Performer,
            Description = programInfo.Description,
            ProgramUrl = programInfo.ProgramUrl
        };

        // ファイル情報
        var recordFile = new RecordingFile
        {
            RecordingId = id,
            FileRelativePath = path.RelativePath,
            HasHlsFile = false
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.Recordings.AddAsync(recording, cancellationToken);
            await dbContext.RecordingMetadatas.AddAsync(recordMetadata, cancellationToken);
            await dbContext.RecordingFiles.AddAsync(recordFile, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.ZLogError(ex, $"録音レコードの作成に失敗しました。");
            throw;
        }

        return id;
    }

    /// <summary>
    /// 録音状態を更新する
    /// </summary>
    public async ValueTask UpdateStateAsync(Ulid recordingId, RecordingState state, string? errorMessage = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var recording = await dbContext.Recordings.FindAsync([recordingId], cancellationToken);
            if (recording == null)
            {
                throw new InvalidOperationException("録音レコードが存在しないため状態を更新できません。");
            }

            recording.State = state;
            recording.ErrorMessage = errorMessage;
            recording.UpdatedAt = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.ZLogError(ex, $"録音状態の更新に失敗しました。");
            throw;
        }
    }

    /// <summary>
    /// 録音ファイルパスを更新する
    /// </summary>
    public async ValueTask UpdateFilePathAsync(Ulid recordingId, MediaPath path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path.RelativePath))
        {
            throw new InvalidOperationException("録音ファイルの相対パスが空です。");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var recordingFile = await dbContext.RecordingFiles.FindAsync([recordingId], cancellationToken);
            if (recordingFile == null)
            {
                throw new InvalidOperationException("録音ファイル情報が存在しないためパスを更新できません。");
            }

            recordingFile.FileRelativePath = path.RelativePath;

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.ZLogError(ex, $"録音ファイルパスの更新に失敗しました。");
            throw;
        }
    }
}
