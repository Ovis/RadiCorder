using RadiCorder.Logics.RdbContext;

using static RadiCorder.Logics.Infrastructure.Configuration.AppConfigurationValues;

namespace RadiCorder.Logics.Services
{
    public partial class AppConfigurationService
    {
        public async ValueTask UpdateStorageLowSpaceThresholdMbAsync(int thresholdMb)
        {
            if (thresholdMb <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(thresholdMb), "thresholdMb must be greater than zero.");
            }

            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(dbContext, AppConfigurationNames.StorageLowSpaceThresholdMb, thresholdMb);

            lock (_lock)
            {
                StorageLowSpaceThresholdMb = thresholdMb;
            }
        }

        public async ValueTask UpdateMonitoringSettingsAsync(int logRetentionDays, int checkIntervalMinutes, int notificationCooldownHours)
        {
            if (logRetentionDays <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(logRetentionDays), "logRetentionDays must be greater than zero.");
            }
            if (checkIntervalMinutes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(checkIntervalMinutes), "checkIntervalMinutes must be greater than zero.");
            }
            if (notificationCooldownHours <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(notificationCooldownHours), "notificationCooldownHours must be greater than zero.");
            }

            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(dbContext, AppConfigurationNames.LogRetentionDays, logRetentionDays);
            await UpsertIntAsync(dbContext, AppConfigurationNames.StorageLowSpaceCheckIntervalMinutes, checkIntervalMinutes);
            await UpsertIntAsync(dbContext, AppConfigurationNames.StorageLowSpaceNotificationCooldownHours, notificationCooldownHours);

            lock (_lock)
            {
                LogRetentionDays = logRetentionDays;
                StorageLowSpaceCheckIntervalMinutes = checkIntervalMinutes;
                StorageLowSpaceNotificationCooldownHours = notificationCooldownHours;
            }
        }

        public async ValueTask UpdateClockSkewMonitoringSettingsAsync(bool enabled, int checkIntervalHours, int thresholdSeconds)
        {
            if (checkIntervalHours <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(checkIntervalHours), "checkIntervalHours must be greater than zero.");
            }
            if (thresholdSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(thresholdSeconds), "thresholdSeconds must be greater than zero.");
            }

            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(dbContext, AppConfigurationNames.ClockSkewMonitoringEnabled, enabled ? 1 : 0);
            await UpsertIntAsync(dbContext, AppConfigurationNames.ClockSkewCheckIntervalHours, checkIntervalHours);
            await UpsertIntAsync(dbContext, AppConfigurationNames.ClockSkewThresholdSeconds, thresholdSeconds);

            lock (_lock)
            {
                ClockSkewMonitoringEnabled = enabled;
                ClockSkewCheckIntervalHours = checkIntervalHours;
                ClockSkewThresholdSeconds = thresholdSeconds;
            }
        }
    }
}
