using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Context;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using ZLogger;

namespace RadiCorder.Logics.Logics.ProgramScheduleLogic
{
    public partial class ProgramScheduleLobLogic(
        ILogger<ProgramScheduleLobLogic> logger,
        IRadioAppContext appContext,
        IRadikoApiClient radikoApiClient,
        IRadiruApiClient radiruApiClient,
        IProgramScheduleRepository programScheduleRepository,
        RecordJobLobLogic recordJobLobLogic,
        IEntryMapper entryMapper,
        NotificationLobLogic? notificationLobLogic = null)
    {
        /// <summary>
        /// 指定されたIDの番組情報を取得
        /// </summary>
        /// <param name="programId"></param>
        /// <param name="serviceKind"></param>
        /// <returns></returns>
        public async ValueTask<RadioProgramEntry?> GetProgramAsync(string programId, RadioServiceKind serviceKind)
        {
            RadioProgramEntry? program = serviceKind switch
            {
                RadioServiceKind.Radiko => await GetRadikoProgramAsync(programId),
                RadioServiceKind.Radiru => await GetRadiruProgramAsync(programId),
                _ => null
            };

            return program;
        }

        /// <summary>
        /// 番組表更新が24時間以内に行われたかを確認
        /// </summary>
        /// <returns></returns>
        public async ValueTask<bool> HasProgramScheduleBeenUpdatedWithin24Hours()
        {
            var lastUpdated = await programScheduleRepository.GetLastUpdatedProgramAsync();

            if (lastUpdated == null)
            {
                return false;
            }

            return lastUpdated.Value.UtcDateTime.UtcToJst().AddHours(24) > appContext.StandardDateTimeOffset;
        }

        /// <summary>
        /// DBに保存された録音スケジュールをもとにスケジュールジョブを作成
        /// </summary>
        /// <returns></returns>
        public async ValueTask SetScheduleJobFromDbAsync()
        {
            var scheduleJobs = await programScheduleRepository.GetScheduleJobsAsync();

            if (scheduleJobs.Count == 0)
            {
                return;
            }

            var failedRestoreJobIds = new List<Ulid>();
            var disabledJobIds = new List<Ulid>();
            var disableFailedJobIds = new List<Ulid>();

            foreach (var scheduleJob in scheduleJobs.Where(x => x.IsEnabled && x.State == ScheduleJobState.Pending))
            {
                var (isSuccess, error) = await recordJobLobLogic.SetScheduleJobAsync(scheduleJob);
                if (isSuccess)
                {
                    continue;
                }

                failedRestoreJobIds.Add(scheduleJob.Id);
                logger.ZLogError(error, $"起動時の録音ジョブ復元に失敗しました。 jobId={scheduleJob.Id}");

                try
                {
                    var disabled = await programScheduleRepository.DisableScheduleJobAsync(scheduleJob.Id);
                    if (disabled)
                    {
                        disabledJobIds.Add(scheduleJob.Id);
                    }
                    else
                    {
                        disableFailedJobIds.Add(scheduleJob.Id);
                        logger.ZLogWarning($"復元失敗ジョブの無効化対象が見つかりませんでした。 jobId={scheduleJob.Id}");
                    }
                }
                catch (Exception disableEx)
                {
                    disableFailedJobIds.Add(scheduleJob.Id);
                    logger.ZLogError(disableEx, $"復元失敗ジョブの無効化に失敗しました。 jobId={scheduleJob.Id}");
                }
            }

            if (failedRestoreJobIds.Count > 0)
            {
                logger.ZLogWarning(
                    $"起動時ジョブ復元で失敗: {failedRestoreJobIds.Count}件, 無効化成功: {disabledJobIds.Count}件, 無効化失敗: {disableFailedJobIds.Count}件");

                if (notificationLobLogic != null)
                {
                    var failedRestoreIds = string.Join(", ", failedRestoreJobIds.Take(10));
                    var failedRestoreSuffix = failedRestoreJobIds.Count > 10 ? " ..." : string.Empty;
                    var disabledIds = string.Join(", ", disabledJobIds.Take(10));
                    var disabledSuffix = disabledJobIds.Count > 10 ? " ..." : string.Empty;
                    var disableFailedIds = string.Join(", ", disableFailedJobIds.Take(10));
                    var disableFailedSuffix = disableFailedJobIds.Count > 10 ? " ..." : string.Empty;
                    await notificationLobLogic.SetNotificationAsync(
                        logLevel: LogLevel.Warning,
                        category: NoticeCategory.SystemError,
                        message:
                        $"起動時ジョブ復元で失敗: {failedRestoreJobIds.Count}件, 無効化成功: {disabledJobIds.Count}件, 無効化失敗: {disableFailedJobIds.Count}件。" +
                        $"復元失敗jobId={failedRestoreIds}{failedRestoreSuffix}; " +
                        $"無効化成功jobId={disabledIds}{disabledSuffix}; " +
                        $"無効化失敗jobId={disableFailedIds}{disableFailedSuffix}");
                }
            }
        }





        /// <summary>
        /// 番組表の最終更新日時を保存
        /// </summary>
        /// <returns></returns>
        public async ValueTask SetProgramLastUpdateDateTimeAsync()
        {
            try
            {
                await programScheduleRepository.SetLastUpdatedProgramAsync(appContext.StandardDateTimeOffset);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"番組表更新日時の更新に失敗");
                throw;
            }
        }
    }
}
