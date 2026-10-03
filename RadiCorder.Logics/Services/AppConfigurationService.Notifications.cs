using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using ZLogger;

using static RadiCorder.Logics.Infrastructure.Configuration.AppConfigurationValues;

namespace RadiCorder.Logics.Services
{
    public partial class AppConfigurationService
    {
        public async ValueTask UpdateNoticeSettingAsync(
            string discordWebhookUrl,
            List<int> selectedNoticeCategories)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertStringAsync(dbContext, AppConfigurationNames.DiscordWebhookUrl, discordWebhookUrl);
                await UpsertStringAsync(dbContext, AppConfigurationNames.NoticeCategories, string.Join(",", selectedNoticeCategories));

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateDuration.");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                DiscordWebhookUrl = discordWebhookUrl;
                NoticeCategories = NormalizeNoticeCategories(selectedNoticeCategories);
            }
        }

        public async ValueTask UpdateUnreadBadgeNoticeCategoriesAsync(List<int> selectedNoticeCategories)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertStringAsync(
                    dbContext,
                    AppConfigurationNames.UnreadBadgeNoticeCategories,
                    string.Join(",", selectedNoticeCategories));

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateUnreadBadgeNoticeCategoriesAsync.");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                UnreadBadgeNoticeCategories = NormalizeNoticeCategories(selectedNoticeCategories);
            }
        }

        private static List<NoticeCategory> NormalizeNoticeCategories(IEnumerable<int> selectedNoticeCategories)
        {
            return selectedNoticeCategories
                .Select(value => (NoticeCategory)value)
                .Where(value => value != NoticeCategory.Undefined)
                .Where(value => Enum.IsDefined(typeof(NoticeCategory), value))
                .Distinct()
                .ToList();
        }

        private static List<NoticeCategory> ParseNoticeCategories(string raw, List<NoticeCategory> fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return fallback;
            }

            var values = raw.Split(',')
                .Select(s => int.TryParse(s, out var value) ? value : default(int?))
                .Where(value => value.HasValue)
                .Select(value => (NoticeCategory)value!.Value)
                .Where(value => value != NoticeCategory.Undefined)
                .Where(value => Enum.IsDefined(typeof(NoticeCategory), value))
                .Distinct()
                .ToList();

            return values.Count == 0 ? fallback : values;
        }
    }
}
