using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Logics.Logics.ProgramScheduleLogic
{
    public partial class ProgramScheduleLobLogic
    {
        /// <summary>
        /// 指定された時間に放送されているradikoの番組表情報リストを取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<List<RadioProgramEntry>> GetRadikoNowOnAirProgramListAsync(DateTimeOffset standardDateTimeOffset)
        {
            var list = await programScheduleRepository.GetRadikoNowOnAirAsync(standardDateTimeOffset);

            return list.Select(p => entryMapper.ToRadioProgramEntry(p)).ToList();
        }

        /// <summary>
        /// radikoの番組表情報リストを取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<List<RadioProgramEntry>> GetRadikoProgramListAsync(DateOnly date, string stationId)
        {
            var list = await programScheduleRepository.GetRadikoProgramsAsync(date, stationId);

            return list.Select(p => entryMapper.ToRadioProgramEntry(p)).ToList();
        }


        /// <summary>
        /// radikoの番組情報を取得
        /// </summary>
        /// <returns></returns>
        public async ValueTask<RadioProgramEntry?> GetRadikoProgramAsync(string programId)
        {
            var program = await programScheduleRepository.GetRadikoProgramByIdAsync(programId);

            if (program == null)
            {
                return null;
            }

            return entryMapper.ToRadioProgramEntry(program);
        }



        /// <summary>
        /// radikoの番組表情報を更新
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async ValueTask UpdateLatestRadikoProgramDataAsync()
        {
            try
            {
                var stationIdList = await programScheduleRepository.GetRadikoStationIdsAsync();

                foreach (var stationId in stationIdList)
                {
                    var programList = await radikoApiClient.GetWeeklyProgramsAsync(stationId);

                    await programScheduleRepository.AddRadikoProgramsIfMissingAsync(programList);
                }
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"radikoの番組表情報更新処理で例外発生");
                throw;
            }
        }

        /// <summary>
        /// 全放送局について、十分先までradiko番組表データが揃っているかを判定
        /// </summary>
        /// <param name="minimumFutureDays">本日から最低何日先まで揃っている必要があるか（既定: 6日）</param>
        public async ValueTask<bool> HasRadikoProgramsForAllStationsThroughAsync(int minimumFutureDays = 6)
        {
            if (minimumFutureDays < 0)
            {
                minimumFutureDays = 0;
            }

            var targetDate = appContext.StandardDateTimeOffset.ToRadioDate().AddDays(minimumFutureDays);
            return await programScheduleRepository.HasRadikoProgramsForAllStationsThroughAsync(targetDate);
        }




        /// <summary>
        /// radiko用番組表情報を検索
        /// </summary>
        /// <param name="searchEntity"></param>
        /// <returns></returns>
        public async ValueTask<List<RadikoProgram>> SearchRadikoProgramAsync(ProgramSearchEntity searchEntity)
        {
            try
            {
                return await programScheduleRepository.SearchRadikoProgramsAsync(searchEntity, appContext.StandardDateTimeOffset);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"番組検索に失敗しました。");
                return [];
            }
        }


        /// <summary>
        /// 古いradikoの番組表データを削除
        /// </summary>
        /// <returns></returns>
        public async ValueTask DeleteOldRadikoProgramAsync()
        {
            try
            {
                var deleteDate = appContext.StandardDateTimeOffset.AddMonths(-1).ToRadioDate();
                await programScheduleRepository.DeleteOldRadikoProgramsAsync(deleteDate);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"radikoの過去の番組データ削除に失敗");
            }
        }
    }
}
