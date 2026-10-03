using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Options;
using RadiCorder.Logics.Primitives;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.RdbContext;
using ZLogger;

using static RadiCorder.Logics.Infrastructure.Configuration.AppConfigurationValues;

namespace RadiCorder.Logics.Services
{
    public partial class AppConfigurationService
    {
        public async ValueTask UpdateReleaseCheckIntervalDaysAsync(int intervalDays)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.ReleaseCheckIntervalDays,
                intervalDays);

            lock (_lock)
            {
                ReleaseCheckIntervalDays = intervalDays;
            }
        }

        public async ValueTask UpdateDuplicateDetectionIntervalDaysAsync(int intervalDays)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.DuplicateDetectionIntervalDays,
                intervalDays);

            lock (_lock)
            {
                DuplicateDetectionIntervalDays = intervalDays;
            }
        }

        public async ValueTask UpdateDuplicateDetectionScheduleAsync(bool enabled, int dayOfWeek, int hour, int minute)
        {
            if (dayOfWeek is < 0 or > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(dayOfWeek), "dayOfWeek must be between 0 and 6.");
            }
            if (hour is < 0 or > 23)
            {
                throw new ArgumentOutOfRangeException(nameof(hour), "hour must be between 0 and 23.");
            }
            if (minute is < 0 or > 59)
            {
                throw new ArgumentOutOfRangeException(nameof(minute), "minute must be between 0 and 59.");
            }

            using var scope = CreateDbContextScope(out var dbContext);

            // 定期実行の有効/無効は intervalDays(0/7) で管理する。
            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.DuplicateDetectionIntervalDays,
                enabled ? 7 : 0);
            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.DuplicateDetectionScheduleDayOfWeek,
                dayOfWeek);
            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.DuplicateDetectionScheduleHour,
                hour);
            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.DuplicateDetectionScheduleMinute,
                minute);

            lock (_lock)
            {
                DuplicateDetectionIntervalDays = enabled ? 7 : 0;
                DuplicateDetectionScheduleDayOfWeek = dayOfWeek;
                DuplicateDetectionScheduleHour = hour;
                DuplicateDetectionScheduleMinute = minute;
            }
        }
    }
}
