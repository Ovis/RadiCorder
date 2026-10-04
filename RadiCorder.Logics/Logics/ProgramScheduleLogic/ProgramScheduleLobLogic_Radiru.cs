using RadiCorder.Logics.Providers.Radiru;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Logics.ProgramScheduleLogic
{
    public partial class ProgramScheduleLobLogic
    {
        /// <summary>
        /// 指定時刻に放送されているらじる★らじるの番組表情報リストを取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<List<RadioProgramEntry>> GetRadiruNowOnAirProgramListAsync(DateTimeOffset standardDateTimeOffset)
        {
            var list = await programScheduleRepository.GetRadiruNowOnAirAsync(standardDateTimeOffset);
            return list.Select(entryMapper.ToRadioProgramEntry).ToList();
        }

        /// <summary>
        /// らじる★らじるの番組表情報を取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<List<RadioProgramEntry>> GetRadiruProgramAsync(DateOnly date, string areaId, string stationId)
        {
            var list = await programScheduleRepository.GetRadiruProgramsAsync(date, areaId, stationId);

            return list.Select(entryMapper.ToRadioProgramEntry).ToList();
        }


        /// <summary>
        /// らじる★らじるの番組情報を取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<RadioProgramEntry?> GetRadiruProgramAsync(string programId)
        {
            var program = await programScheduleRepository.GetRadiruProgramByIdAsync(programId);

            if (program == null)
            {
                return null;
            }

            return entryMapper.ToRadioProgramEntry(program);
        }



        public async ValueTask UpdateRadiruProgramDataAsync()
            => (await SynchronizeRadiruProgramsAsync(default)).ThrowIfFailed();

        public async ValueTask<ProgramSyncReport> SynchronizeRadiruProgramsAsync(CancellationToken cancellationToken)
        {
            var report = new ProgramSyncReport();
            var dateList = Enumerable.Range(-6, 15)
                .Select(i => appContext.StandardDateTimeOffset.AddDays(-i))
                .ToList();
            var hasAnyTarget = false;

            try
            {
                foreach (var dateTimeOffset in dateList)
                {
                    var areaServices = await radiruApiClient.GetAvailableAreaServicesAsync(dateTimeOffset, cancellationToken);
                    if (areaServices.Count == 0)
                    {
                        continue;
                    }

                    hasAnyTarget = true;

                    foreach (var (areaId, serviceId) in areaServices.Distinct())
                    {
                        await report.RunAsync($"{areaId}:{serviceId}:{dateTimeOffset:yyyy-MM-dd}", async () =>
                        {
                            await UpsertDailyProgramDataCoreAsync(areaId, serviceId, dateTimeOffset, cancellationToken);
                        }, cancellationToken);
                    }
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"らじる\u2605らじるの番組表情報更新で例外発生");
                throw;
            }

            if (!hasAnyTarget)
            {
                logger.ZLogWarning($"らじる★らじるの取得対象サービスが存在しないため番組表更新をスキップしました。");
            }
            return report;
        }


        public async ValueTask DeleteOldRadiruProgramAsync()
        {
            try
            {
                var deleteDate = appContext.StandardDateTimeOffset.AddMonths(-1).ToRadioDate();
                await programScheduleRepository.DeleteOldRadiruProgramsAsync(deleteDate);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"らじる\u2605らじるの過去の番組データ削除に失敗");
                throw;
            }
        }


        private async ValueTask<bool> UpsertDailyProgramDataCoreAsync(string areaId, string serviceId, DateTimeOffset dt, CancellationToken cancellationToken)
        {
            var programList = await radiruApiClient.GetDailyProgramsAsync(areaId, serviceId, dt, cancellationToken);

            if (!programList.Any())
            {
                return false;
            }

            try
            {
                var entries = new List<NhkRadiruProgram>();

                foreach (var programJsonEntity in programList)
                {
                    if (!RadiruProgramNormalizer.TryNormalize(areaId, serviceId, programJsonEntity, out var entry))
                    {
                        logger.ZLogWarning(
                            $"らじる★らじる番組を必須項目不足でスキップ areaId={areaId} stationId={serviceId} programId={programJsonEntity.Id}");
                        throw new DomainException("らじる★らじる番組表の必須項目が不足しています。既存データを保持します。");
                    }

                    entries.Add(entry);
                }

                if (entries.Count == 0)
                {
                    logger.ZLogWarning($"らじる★らじる番組で保存可能なエントリがありませんでした areaId={areaId} stationId={serviceId} date={dt:yyyy-MM-dd}");
                    return false;
                }

                await programScheduleRepository.UpsertRadiruProgramsAsync(entries, cancellationToken);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"番組表更新処理に失敗");
                throw;
            }

            return true;
        }

        /// <summary>
        /// らじる★らじる番組表検索
        /// </summary>
        /// <param name="searchEntity"></param>
        /// <returns></returns>
        public async ValueTask<List<NhkRadiruProgram>> SearchRadiruProgramAsync(ProgramSearchEntity searchEntity)
        {
            try
            {
                return await programScheduleRepository.SearchRadiruProgramsAsync(searchEntity, appContext.StandardDateTimeOffset);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"番組検索に失敗しました。");
                throw;
            }
        }
    }
}
