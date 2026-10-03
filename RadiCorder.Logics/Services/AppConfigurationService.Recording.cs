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
        public async ValueTask UpdateRecordDirectoryPathAsync(string recordDirectoryPath)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertStringAsync(dbContext, AppConfigurationNames.RecordDirectoryPath, recordDirectoryPath);
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateRecordDirectoryPath");
                await transaction.RollbackAsync();
                throw;
            }


            lock (_lock)
            {
                RecordDirectoryRelativePath = recordDirectoryPath;
            }
        }

        public async ValueTask UpdateRecordFileNameTemplateAsync(string fileNameTemplate)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertStringAsync(dbContext, AppConfigurationNames.RecordFileNameTemplate, fileNameTemplate);
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateRecordFileNameTemplate");
                await transaction.RollbackAsync();
                throw;
            }


            lock (_lock)
            {
                RecordFileNameTemplate = fileNameTemplate;
            }
        }

        public async ValueTask UpdateDurationAsync(int startDuration, int endDuration)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertIntAsync(dbContext, AppConfigurationNames.RecordStartDuration, startDuration);
                await UpsertIntAsync(dbContext, AppConfigurationNames.RecordEndDuration, endDuration);

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
                RecordStartDuration = new TimeSpan(0, 0, startDuration);
                RecordEndDuration = new TimeSpan(0, 0, endDuration);
            }
        }

        public async ValueTask UpdateMergeTagsFromAllMatchedKeywordRulesAsync(bool enabled)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.MergeTagsFromAllMatchedKeywordRules,
                enabled ? 1 : 0);

            lock (_lock)
            {
                MergeTagsFromAllMatchedKeywordRules = enabled;
            }
        }

        public async ValueTask UpdateEmbedProgramImageOnRecordAsync(bool enabled)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.EmbedProgramImageOnRecord,
                enabled ? 1 : 0);

            lock (_lock)
            {
                EmbedProgramImageOnRecord = enabled;
            }
        }

        public async ValueTask UpdateResumePlaybackAcrossPagesAsync(bool enabled)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await UpsertIntAsync(
                dbContext,
                AppConfigurationNames.ResumePlaybackAcrossPages,
                enabled ? 1 : 0);

            lock (_lock)
            {
                ResumePlaybackAcrossPages = enabled;
            }
        }
    }
}
