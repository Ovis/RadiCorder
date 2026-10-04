using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Logics.RecordJobLogic;

/// <summary>
/// 中断状態を保持したまま、再投入・確定待ち・失敗を判断する。
/// </summary>
public class RecordingJobRecovery(RadioDbContext dbContext, IAppConfigurationService config, RecordingFinalizationJournal? journal = null)
{
    public async ValueTask RecoverAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var blockedJobs = new HashSet<string>(StringComparer.Ordinal);
        var hasUnknownEntry = false;
        if (journal != null)
        {
            foreach (var file in journal.GetPendingFiles())
            {
                // 壊れた復旧情報がある場合も、全体を再投入して重複録音を起こさない。
                try
                {
                    var entry = journal.Read(file);
                    if (entry.ScheduleJobId != null) blockedJobs.Add(entry.ScheduleJobId);
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or ArgumentException)
                {
                    hasUnknownEntry = true;
                }
            }
        }
        var targets = await dbContext.ScheduleJob.Where(x => x.IsEnabled).ToListAsync(cancellationToken);
        foreach (var job in targets)
        {
            if (job.State is ScheduleJobState.Completed or ScheduleJobState.Failed or ScheduleJobState.Cancelled)
            {
                job.IsEnabled = false;
                continue;
            }
            if (hasUnknownEntry || blockedJobs.Contains(job.Id.ToString()))
            {
                job.State = ScheduleJobState.Finalizing;
                job.LastErrorCode = ScheduleJobErrorCode.FinalizeFailed;
                job.LastErrorDetail = "保存済み録音の確定待ちです。復旧情報を確認してください。";
                job.IsEnabled = false;
                continue;
            }
            if (job.State == ScheduleJobState.Pending) continue;
            if (job.State == ScheduleJobState.Finalizing)
            {
                // 旧バージョンのジョブには録音IDの復旧記録がないため、自動再録音しない。
                job.State = ScheduleJobState.Failed;
                job.LastErrorCode = ScheduleJobErrorCode.FinalizeFailed;
                job.LastErrorDetail = "後処理中に停止しました。保存済みファイルを確認してください。";
                job.CompletedUtc = nowUtc;
                job.IsEnabled = false;
                continue;
            }
            var startedAt = job.ActualStartUtc ?? job.QueuedAtUtc ?? job.PrepareStartUtc;
            if (startedAt == default) startedAt = job.StartDateTime.ToUniversalTime();
            if (nowUtc - startedAt > TimeSpan.FromHours(2))
            {
                job.State = ScheduleJobState.Failed;
                job.LastErrorCode = ScheduleJobErrorCode.StartupRecoveryTimeout;
                job.LastErrorDetail = "起動時復旧でタイムアウトしたため失敗扱いにしました。";
                job.CompletedUtc = nowUtc;
                job.IsEnabled = false;
                continue;
            }
            var fireAt = RecordingScheduleTiming.ResolveFireAtUtc(job.RecordingType, job.StartDateTime, job.EndDateTime, job.StartDelay ?? config.RecordStartDuration, nowUtc);
            if (fireAt == null) throw new InvalidDataException("復旧対象の録音方法が不正です。");
            job.State = ScheduleJobState.Pending;
            job.QueuedAtUtc = null;
            job.ActualStartUtc = null;
            job.CompletedUtc = null;
            job.PrepareStartUtc = fireAt.Value - RecordingScheduleTiming.PreparingLeadTime;
            if (job.PrepareStartUtc < nowUtc) job.PrepareStartUtc = nowUtc;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
