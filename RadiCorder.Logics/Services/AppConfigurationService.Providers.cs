using RadiCorder.Logics.RdbContext;
using ZLogger;

using static RadiCorder.Logics.Infrastructure.Configuration.AppConfigurationValues;

namespace RadiCorder.Logics.Services
{
    public partial class AppConfigurationService
    {
        /// <summary>
        /// らじる★らじる録音対象エリア情報を更新
        /// </summary>
        /// <param name="areaId"></param>
        /// <returns></returns>
        public async ValueTask UpdateRadiruAreaAsync(string areaId)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();


            try
            {
                await UpsertStringAsync(dbContext, AppConfigurationNames.RadiruArea, areaId);
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateRadiruArea");
                await transaction.RollbackAsync();
                throw;
            }


            lock (_lock)
            {
                RadiruArea = areaId;
            }
        }

        public async ValueTask UpdateExternalServiceUserAgentAsync(string userAgent)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertStringAsync(dbContext, AppConfigurationNames.ExternalServiceUserAgent, userAgent);
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateExternalServiceUserAgent");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                ExternalServiceUserAgent = userAgent;
            }
        }

        public async ValueTask UpdateRadiruApiRequestSettingsAsync(int minRequestIntervalMs, int requestJitterMs)
        {
            if (minRequestIntervalMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minRequestIntervalMs), "minRequestIntervalMs must be greater than or equal to zero.");
            }
            if (requestJitterMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(requestJitterMs), "requestJitterMs must be greater than or equal to zero.");
            }

            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(dbContext, AppConfigurationNames.RadiruApiMinRequestIntervalMs, minRequestIntervalMs);
            await UpsertIntAsync(dbContext, AppConfigurationNames.RadiruApiRequestJitterMs, requestJitterMs);

            lock (_lock)
            {
                RadiruApiMinRequestIntervalMs = minRequestIntervalMs;
                RadiruApiRequestJitterMs = requestJitterMs;
            }
        }

        public async ValueTask UpdateExternalImportFileTimeZoneAsync(string timeZoneId)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                await UpsertStringAsync(dbContext, AppConfigurationNames.ExternalImportFileTimeZoneId, timeZoneId);
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateExternalImportFileTimeZoneAsync.");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                ExternalImportFileTimeZoneId = timeZoneId;
            }
        }
    }
}
