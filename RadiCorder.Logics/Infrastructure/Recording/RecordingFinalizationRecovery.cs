using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.RecordingLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Infrastructure.Recording;

/// <summary>
/// 保存済みファイルのDB確定を、再録音せずに再開する。
/// </summary>
public class RecordingFinalizationRecovery(
    RecordingFinalizationJournal journal,
    IRecordingRepository repository,
    RadioDbContext dbContext,
    RecordingLobLogic recordingLogic,
    ILogger<RecordingFinalizationRecovery> logger)
{
    public async ValueTask RecoverAsync(CancellationToken cancellationToken)
    {
        foreach (var filePath in journal.GetPendingFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var entry = journal.Read(filePath);
                var recording = await dbContext.Recordings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entry.RecordingId, cancellationToken)
                    ?? throw new InvalidDataException("復旧対象の録音レコードがありません。");
                if (!File.Exists(entry.Path.FinalFilePath) || File.Exists(entry.Path.TempFilePath))
                {
                    throw new InvalidDataException("録音ファイルの移動が確定していません。復旧情報とファイルを保持します。");
                }

                ScheduleJob? job = null;
                if (entry.ScheduleJobId != null)
                {
                    if (!Ulid.TryParse(entry.ScheduleJobId, out var expectedJobId)) throw new InvalidDataException("復旧対象ジョブIDが不正です。");
                    job = await dbContext.ScheduleJob.AsNoTracking().SingleOrDefaultAsync(x => x.Id == expectedJobId, cancellationToken);
                    if (job != null && (job.ProgramId != recording.ProgramId || job.ServiceKind != recording.ServiceKind))
                        throw new InvalidDataException("録音と復旧対象ジョブの識別子が一致しません。");
                }
                await repository.UpdateFilePathAsync(entry.RecordingId, entry.Path, cancellationToken);
                await repository.UpdateStateAsync(entry.RecordingId, RecordingState.Completed, null, cancellationToken);
                if (Ulid.TryParse(entry.ScheduleJobId, out var jobId))
                {
                    if (job != null)
                    {
                        await recordingLogic.TryApplyKeywordReserveTagsAsync(entry.ScheduleJobId!, entry.RecordingId);
                        await dbContext.ScheduleJob.Where(x => x.Id == jobId).ExecuteUpdateAsync(setters => setters
                            .SetProperty(x => x.State, ScheduleJobState.Completed)
                            .SetProperty(x => x.CompletedUtc, DateTimeOffset.UtcNow)
                            .SetProperty(x => x.LastErrorCode, ScheduleJobErrorCode.None)
                            .SetProperty(x => x.LastErrorDetail, (string?)null)
                            .SetProperty(x => x.IsEnabled, false), cancellationToken);
                    }
                }
                journal.Complete(entry.RecordingId);
                logger.ZLogInformation($"保存済み録音を復旧しました。 recordingId={entry.RecordingId}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 復旧に失敗した情報は削除せず、次回起動と手動確認に残す。
                logger.ZLogError(ex, $"録音確定の復旧に失敗しました。 journal={filePath}");
            }
        }
    }
}
