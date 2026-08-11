using RadiCorder.Logics.Extensions;
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
        {
            var dateList = Enumerable.Range(-6, 15)
                .Select(i => appContext.StandardDateTimeOffset.AddDays(-i))
                .ToList();
            var hasAnyTarget = false;

            try
            {
                foreach (var dateTimeOffset in dateList)
                {
                    var areaServices = await radiruApiClient.GetAvailableAreaServicesAsync(dateTimeOffset);
                    if (areaServices.Count == 0)
                    {
                        continue;
                    }

                    hasAnyTarget = true;

                    foreach (var (areaId, serviceId) in areaServices.Distinct())
                    {
                        await UpsertDailyProgramDataAsync(areaId, serviceId, dateTimeOffset);
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
            }
        }


        private async ValueTask<bool> UpsertDailyProgramDataAsync(string areaId, string serviceId, DateTimeOffset dt)
        {
            var programList = await radiruApiClient.GetDailyProgramsAsync(areaId, serviceId, dt);

            if (!programList.Any())
            {
                return false;
            }

            try
            {
                var entries = new List<NhkRadiruProgram>();

                foreach (var programJsonEntity in programList)
                {
                    if (!TryCreateRadiruProgramEntry(areaId, serviceId, programJsonEntity, out var entry))
                    {
                        logger.ZLogWarning(
                            $"らじる★らじる番組を必須項目不足でスキップ areaId={areaId} stationId={serviceId} programId={programJsonEntity.Id}");
                        continue;
                    }

                    entries.Add(entry);
                }

                if (entries.Count == 0)
                {
                    logger.ZLogWarning($"らじる★らじる番組で保存可能なエントリがありませんでした areaId={areaId} stationId={serviceId} date={dt:yyyy-MM-dd}");
                    return false;
                }

                await programScheduleRepository.UpsertRadiruProgramsAsync(entries);
            }
            catch (Exception e)
            {
                logger.ZLogError(e, $"番組表更新処理に失敗");
                throw;
            }

            return true;
        }

        private static bool TryCreateRadiruProgramEntry(
            string areaId,
            string serviceId,
            RadiruProgramJsonEntity programJsonEntity,
            out NhkRadiruProgram entry)
        {
            var hasRequiredFieldError = false;
            var title = programJsonEntity.GetTitle();

            if (string.IsNullOrWhiteSpace(programJsonEntity.Id))
            {
                hasRequiredFieldError = true;
            }

            if (programJsonEntity.StartDate == default)
            {
                hasRequiredFieldError = true;
            }

            if (programJsonEntity.EndDate == default)
            {
                hasRequiredFieldError = true;
            }

            if (programJsonEntity.StartDate != default &&
                programJsonEntity.EndDate != default &&
                programJsonEntity.EndDate <= programJsonEntity.StartDate)
            {
                hasRequiredFieldError = true;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                hasRequiredFieldError = true;
            }

            if (hasRequiredFieldError)
            {
                entry = new NhkRadiruProgram();
                return false;
            }

            var onDemandContentUrl = SelectOnDemandContentUrl(programJsonEntity.About.Audio);
            var onDemandExpiresAtUtc = programJsonEntity.About.Audio.Expires == default
                ? (DateTime?)null
                : programJsonEntity.About.Audio.Expires.UtcDateTime;

            entry = new NhkRadiruProgram
            {
                ProgramId = $"{programJsonEntity.Id}",
                StationId = serviceId,
                AreaId = areaId,
                RadioDate = programJsonEntity.StartDate.ToRadioDate(),
                DaysOfWeek = programJsonEntity.StartDate.ToRadioDayOfWeek().ToDaysOfWeek(),
                EventId = programJsonEntity.About.Id,
                StartTime = programJsonEntity.StartDate,
                EndTime = programJsonEntity.EndDate,
                Title = title,
                Subtitle = programJsonEntity.IdentifierGroup.RadioEpisodeName.ToSafeName().To半角英数字(),
                Description = programJsonEntity.GetCombinedDescription(),
                Performer = programJsonEntity.GetCombinedActorsAndArtists(),
                SiteId = programJsonEntity.IdentifierGroup.SiteId,
                ImageUrl = programJsonEntity.About.PartOfSeries.Logo.Medium.Url,
                ProgramUrl = programJsonEntity.About.Url,
                OnDemandContentUrl = onDemandContentUrl,
                OnDemandExpiresAtUtc = onDemandExpiresAtUtc
            };

            return true;
        }

        private static string? SelectOnDemandContentUrl(Audio audio)
        {
            var detailedContents = audio.DetailedContent
                .Where(d => !string.IsNullOrWhiteSpace(d.ContentUrl))
                .ToList();

            if (detailedContents.Count == 0)
            {
                return null;
            }

            var prioritized = detailedContents.FirstOrDefault(d =>
                string.Equals(d.Name, "hls_widevine", StringComparison.OrdinalIgnoreCase) &&
                IsM3u8Url(d.ContentUrl));
            if (prioritized is not null)
            {
                return prioritized.ContentUrl;
            }

            var fallback = detailedContents.FirstOrDefault(d => IsM3u8Url(d.ContentUrl));
            return fallback?.ContentUrl;
        }

        private static bool IsM3u8Url(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
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
                return [];
            }
        }
    }
}
