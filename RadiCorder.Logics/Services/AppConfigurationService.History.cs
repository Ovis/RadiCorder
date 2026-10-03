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
        public ValueTask<DateTimeOffset?> GetStorageLowSpaceLastNotifiedAtAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            var raw = GetStringValue(dbContext, AppConfigurationNames.StorageLowSpaceLastNotifiedAtUtc);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ValueTask.FromResult<DateTimeOffset?>(null);
            }

            if (DateTimeOffset.TryParse(raw, out var parsed))
            {
                return ValueTask.FromResult<DateTimeOffset?>(parsed.ToUniversalTime());
            }

            return ValueTask.FromResult<DateTimeOffset?>(null);
        }

        public async ValueTask UpdateStorageLowSpaceLastNotifiedAtAsync(DateTimeOffset utcTimestamp)
        {
            using var scope = CreateDbContextScope(out var dbContext);
            await UpsertStringAsync(
                dbContext,
                AppConfigurationNames.StorageLowSpaceLastNotifiedAtUtc,
                utcTimestamp.ToUniversalTime().ToString("O"));
        }

        public ValueTask<DateTimeOffset?> GetReleaseCheckLastCheckedAtAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            var raw = GetStringValue(dbContext, AppConfigurationNames.ReleaseCheckLastCheckedAtUtc);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ValueTask.FromResult<DateTimeOffset?>(null);
            }

            if (DateTimeOffset.TryParse(raw, out var parsed))
            {
                return ValueTask.FromResult<DateTimeOffset?>(parsed.ToUniversalTime());
            }

            return ValueTask.FromResult<DateTimeOffset?>(null);
        }

        public async ValueTask UpdateReleaseCheckLastCheckedAtAsync(DateTimeOffset utcTimestamp)
        {
            using var scope = CreateDbContextScope(out var dbContext);
            await UpsertStringAsync(
                dbContext,
                AppConfigurationNames.ReleaseCheckLastCheckedAtUtc,
                utcTimestamp.ToUniversalTime().ToString("O"));
        }

        public ValueTask<DateTimeOffset?> GetRadiruStationDefinitionLastCheckedAtAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            var raw = GetStringValue(dbContext, AppConfigurationNames.RadiruStationDefinitionLastCheckedAtUtc);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return ValueTask.FromResult<DateTimeOffset?>(null);
            }

            if (DateTimeOffset.TryParse(raw, out var parsed))
            {
                return ValueTask.FromResult<DateTimeOffset?>(parsed.ToUniversalTime());
            }

            return ValueTask.FromResult<DateTimeOffset?>(null);
        }

        public async ValueTask UpdateRadiruStationDefinitionLastCheckedAtAsync(DateTimeOffset utcTimestamp)
        {
            using var scope = CreateDbContextScope(out var dbContext);
            await UpsertStringAsync(
                dbContext,
                AppConfigurationNames.RadiruStationDefinitionLastCheckedAtUtc,
                utcTimestamp.ToUniversalTime().ToString("O"));
        }

        public ValueTask<string?> GetReleaseLastNotifiedVersionAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);
            return ValueTask.FromResult(GetStringValue(dbContext, AppConfigurationNames.ReleaseLastNotifiedVersion));
        }

        public async ValueTask UpdateReleaseLastNotifiedVersionAsync(string version)
        {
            using var scope = CreateDbContextScope(out var dbContext);
            await UpsertStringAsync(
                dbContext,
                AppConfigurationNames.ReleaseLastNotifiedVersion,
                version);
        }
    }
}
