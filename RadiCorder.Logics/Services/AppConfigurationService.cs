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

namespace RadiCorder.Logics.Services
{
    public class AppConfigurationService : IAppConfigurationService
    {
        private readonly ILogger<AppConfigurationService> _logger;

        private readonly IServiceProvider _serviceProvider;
        private readonly IDataProtector _dataProtector;
        private readonly object _lock = new();

        public RadikoOptions RadikoOptions { get; }
        private StorageOptions StorageOptions { get; }
        private ExternalServiceOptions ExternalServiceOptions { get; }
        private MonitoringOptions MonitoringOptions { get; }
        private AutomationOptions AutomationOptions { get; }
        private ReleaseOptions ReleaseOptions { get; }

        public string FfmpegExecutablePath => StorageOptions.FfmpegExecutablePath;
        public int LogRetentionDays { get; private set; }
        public int StorageLowSpaceCheckIntervalMinutes { get; private set; }
        public int StorageLowSpaceNotificationCooldownHours { get; private set; }
        public bool ClockSkewMonitoringEnabled { get; private set; }
        public int ClockSkewCheckIntervalHours { get; private set; }
        public int ClockSkewThresholdSeconds { get; private set; }
        public string ClockSkewNtpServer { get; private set; } = string.Empty;
        public int RadiruApiMinRequestIntervalMs { get; private set; }
        public int RadiruApiRequestJitterMs { get; private set; }
        public string ReleaseCheckGitHubOwner => ReleaseOptions.ReleaseCheckGitHubOwner;
        public string ReleaseCheckGitHubRepository => ReleaseOptions.ReleaseCheckGitHubRepository;

        public bool IsRadikoPremiumUser { get; private set; }
        public bool IsRadikoAreaFree { get; private set; }
        public bool HasRadikoCredentials { get; private set; }

        public string RecordFileSaveDir { get; private set; }

        public string TemporaryFileSaveDir { get; private set; }

        public string? RecordDirectoryRelativePath { get; private set; }
        public string? RecordFileNameTemplate { get; private set; }

        public TimeSpan RecordStartDuration { get; private set; }

        public TimeSpan RecordEndDuration { get; private set; }

        public string RadiruArea { get; private set; } = string.Empty;
        public string ExternalServiceUserAgent { get; private set; } = string.Empty;

        public string? DiscordWebhookUrl { get; private set; }
        public string ExternalImportFileTimeZoneId { get; private set; } = JapanTimeZone.Resolve().Id;
        public int StorageLowSpaceThresholdMb { get; private set; }
        public bool MergeTagsFromAllMatchedKeywordRules { get; private set; }
        public bool EmbedProgramImageOnRecord { get; private set; }
        public bool ResumePlaybackAcrossPages { get; private set; }
        public int ReleaseCheckIntervalDays { get; private set; }
        public int DuplicateDetectionIntervalDays { get; private set; }
        public int DuplicateDetectionScheduleDayOfWeek { get; private set; }
        public int DuplicateDetectionScheduleHour { get; private set; }
        public int DuplicateDetectionScheduleMinute { get; private set; }
        public List<NoticeCategory> NoticeCategories { get; private set; } = [];
        public List<NoticeCategory> UnreadBadgeNoticeCategories { get; private set; } = [];

        public ConcurrentDictionary<string, string> RadikoStationDic { get; private set; } = new();

        public IMemoryCache Cache { get; set; } =
            new MemoryCache(new MemoryCacheOptions
            {
                ExpirationScanFrequency = TimeSpan.FromSeconds(60),
            });

        public TimeSpan AbsoluteExpirationRelativeToNow { get; set; } = TimeSpan.FromDays(1);


        public AppConfigurationService(
            ILogger<AppConfigurationService> logger,
            IServiceProvider serviceProvider,
            IOptionsMonitor<RadikoOptions> radikoOptions,
            IOptionsMonitor<StorageOptions> storageOptions,
            IOptionsMonitor<ExternalServiceOptions> externalServiceOptions,
            IOptionsMonitor<MonitoringOptions> monitoringOptions,
            IOptionsMonitor<AutomationOptions> automationOptions,
            IOptionsMonitor<ReleaseOptions> releaseOptions,
            IDataProtectionProvider dataProtectionProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            RadikoOptions = radikoOptions.CurrentValue;
            StorageOptions = storageOptions.CurrentValue;
            ExternalServiceOptions = externalServiceOptions.CurrentValue;
            MonitoringOptions = monitoringOptions.CurrentValue;
            AutomationOptions = automationOptions.CurrentValue;
            ReleaseOptions = releaseOptions.CurrentValue;

            _dataProtector = dataProtectionProvider.CreateProtector("RadiCorder.RadikoCredentials.v1");

            var configuredRecordDir = NormalizeStorageDirectory(
                StorageOptions.RecordFileSaveFolder,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "record"));
            var configuredTempDir = NormalizeStorageDirectory(
                StorageOptions.TemporaryFileSaveFolder,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp"));

            // 録音ファイル保存フォルダの設定
            {
                if (!Directory.Exists(configuredRecordDir))
                {
                    logger.ZLogDebug($"録音ファイル保存フォルダ{configuredRecordDir} が存在しないため作成");
                    var recordFileSaveDir = configuredRecordDir;
                    logger.ZLogDebug($"録音ファイル保存フォルダパス：{recordFileSaveDir}");
                    Directory.CreateDirectory(recordFileSaveDir);

                    RecordFileSaveDir = recordFileSaveDir;
                }
                else
                {
                    logger.ZLogDebug($"録音ファイル保存フォルダ{configuredRecordDir} が存在するため保存フォルダとして採用");
                    RecordFileSaveDir = configuredRecordDir;
                }
            }

            // 一時ファイル保存フォルダの設定
            {
                if (Directory.Exists(configuredTempDir))
                {
                    logger.ZLogDebug($"一時フォルダ{configuredTempDir} が存在するため利用する");
                    TemporaryFileSaveDir = configuredTempDir;
                }
                else
                {
                    var temporaryFileSaveFolder = configuredTempDir;
                    logger.ZLogDebug($"一時フォルダ{temporaryFileSaveFolder}を作成し利用する");

                    TemporaryFileSaveDir = temporaryFileSaveFolder;

                    try
                    {
                        Directory.CreateDirectory(temporaryFileSaveFolder);
                    }
                    catch (Exception e)
                    {
                        logger.ZLogError(e, $"一時フォルダ{temporaryFileSaveFolder}の作成に失敗");

                        throw new DomainException("一時フォルダ作成に失敗しました。");
                    }
                }
            }

            // DBから必要な設定値を取得
            InitializeSettings();
        }

        private static string NormalizeStorageDirectory(string? configuredPath, string fallbackPath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return fallbackPath;
            }

            if (Path.IsPathRooted(configuredPath))
            {
                return configuredPath;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath);
        }


        /// <summary>
        /// 放送局名の取得
        /// </summary>
        /// <param name="kind"></param>
        /// <param name="stationId"></param>
        /// <returns></returns>
        public string ChooseStationName(RadioServiceKind kind, string stationId)
        {
            return kind switch
            {
                RadioServiceKind.Radiko => ResolveRadikoStationName(stationId),
                RadioServiceKind.Radiru => ResolveRadiruStationName(stationId),
                _ => throw new DomainException("未対応のサービス種別です。")
            };
        }

        private string ResolveRadikoStationName(string stationId)
        {
            if (RadikoStationDic.TryGetValue(stationId, out var stationName))
            {
                return stationName;
            }

            _logger.ZLogWarning($"radiko局名を解決できませんでした。 stationId={stationId}");
            return $"不明局({stationId})";
        }

        private static string ResolveRadiruStationName(string stationId)
        {
            var station = Enumeration.GetAll<RadiruStationKind>()
                .FirstOrDefault(r => r.ServiceId == stationId);

            return station?.Name ?? $"不明局({stationId})";
        }



        /// <summary>
        /// IsRadikoPremiumUserの値を更新
        /// </summary>
        /// <param name="isRadikoPremiumUser"></param>
        public void UpdateRadikoPremiumUser(bool isRadikoPremiumUser)
        {
            lock (_lock)
            {
                IsRadikoPremiumUser = isRadikoPremiumUser;
            }
        }

        /// <summary>
        /// radikoのエリアフリー利用可否を更新
        /// </summary>
        /// <param name="isRadikoAreaFree"></param>
        public void UpdateRadikoAreaFree(bool isRadikoAreaFree)
        {
            lock (_lock)
            {
                IsRadikoAreaFree = isRadikoAreaFree;
            }
        }

        public async ValueTask UpdateRadikoCredentialsAsync(string userId, string password)
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                var protectedUserId = _dataProtector.Protect(userId);
                var protectedPassword = _dataProtector.Protect(password);

                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoUserIdProtected, protectedUserId);
                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoPasswordProtected, protectedPassword);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed UpdateRadikoCredentials");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                RadikoOptions.RadikoUserId = userId;
                HasRadikoCredentials = true;
            }
        }

        public async ValueTask ClearRadikoCredentialsAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            try
            {
                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoUserIdProtected, string.Empty);
                await UpsertStringAsync(dbContext, AppConfigurationNames.RadikoPasswordProtected, string.Empty);

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"Failed ClearRadikoCredentials");
                await transaction.RollbackAsync();
                throw;
            }

            lock (_lock)
            {
                RadikoOptions.RadikoUserId = string.Empty;
                RadikoOptions.RadikoPassword = string.Empty;
                HasRadikoCredentials = false;
                IsRadikoPremiumUser = false;
                IsRadikoAreaFree = false;
            }
        }

        /// <summary>
        /// 保存済みのradiko資格情報を取得する
        /// </summary>
        /// <returns>取得可否と資格情報</returns>
        public ValueTask<(bool IsSuccess, string UserId, string Password)> TryGetRadikoCredentialsAsync()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            var protectedUserId = GetStringValue(dbContext, AppConfigurationNames.RadikoUserIdProtected) ?? string.Empty;
            var protectedPassword = GetStringValue(dbContext, AppConfigurationNames.RadikoPasswordProtected) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(protectedUserId) || string.IsNullOrWhiteSpace(protectedPassword))
            {
                return ValueTask.FromResult((false, string.Empty, string.Empty));
            }

            try
            {
                var userId = _dataProtector.Unprotect(protectedUserId);
                var password = _dataProtector.Unprotect(protectedPassword);
                return ValueTask.FromResult((true, userId, password));
            }
            catch (Exception ex)
            {
                _logger.ZLogError(ex, $"radikoログイン情報の復号に失敗しました。");
                return ValueTask.FromResult((false, string.Empty, string.Empty));
            }
        }


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


        /// <summary>
        /// radikoの放送局情報キャッシュを更新
        /// </summary>
        /// <param name="stationList"></param>
        /// <returns></returns>
        public void UpdateRadikoStationDic(List<RadikoStation> stationList)
        {
            lock (_lock)
            {
                var normalizedStations = stationList
                    .Where(station => !string.IsNullOrWhiteSpace(station.StationId))
                    .GroupBy(station => station.StationId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToList();

                var newMap = new ConcurrentDictionary<string, string>(
                    normalizedStations.Select(station => new KeyValuePair<string, string>(station.StationId, station.StationName)),
                    StringComparer.OrdinalIgnoreCase);

                RadikoStationDic = newMap;

            }
        }



        /// <summary>
        ///  DBから必要な設定値を取得する
        /// </summary>
        private void InitializeSettings()
        {
            using var scope = CreateDbContextScope(out var dbContext);

            // 録音ファイル保存位置
            RecordDirectoryRelativePath = GetStringValue(dbContext, AppConfigurationNames.RecordDirectoryPath);

            // 録音
            RecordStartDuration = TimeSpan.FromSeconds(GetIntValue(dbContext, AppConfigurationNames.RecordStartDuration) ?? 0);
            RecordEndDuration = TimeSpan.FromSeconds(GetIntValue(dbContext, AppConfigurationNames.RecordEndDuration) ?? 0);

            // DiscordWebhookURL
            DiscordWebhookUrl = GetStringValue(dbContext, AppConfigurationNames.DiscordWebhookUrl);

            // 外部取込時のファイル更新日時タイムゾーン
            var externalImportTimeZoneId = GetStringValue(dbContext, AppConfigurationNames.ExternalImportFileTimeZoneId);
            if (string.IsNullOrWhiteSpace(externalImportTimeZoneId))
            {
                ExternalImportFileTimeZoneId = JapanTimeZone.Resolve().Id;
            }
            else
            {
                try
                {
                    _ = TimeZoneInfo.FindSystemTimeZoneById(externalImportTimeZoneId);
                    ExternalImportFileTimeZoneId = externalImportTimeZoneId;
                }
                catch
                {
                    ExternalImportFileTimeZoneId = JapanTimeZone.Resolve().Id;
                }
            }

            // 保存先ストレージ空き容量通知しきい値（MB）
            StorageLowSpaceThresholdMb =
                GetIntValue(dbContext, AppConfigurationNames.StorageLowSpaceThresholdMb)
                ?? MonitoringOptions.StorageLowSpaceThresholdMb;

            // 監視関連設定
            LogRetentionDays =
                GetIntValue(dbContext, AppConfigurationNames.LogRetentionDays)
                ?? MonitoringOptions.LogRetentionDays;
            StorageLowSpaceCheckIntervalMinutes =
                GetIntValue(dbContext, AppConfigurationNames.StorageLowSpaceCheckIntervalMinutes)
                ?? MonitoringOptions.StorageLowSpaceCheckIntervalMinutes;
            StorageLowSpaceNotificationCooldownHours =
                GetIntValue(dbContext, AppConfigurationNames.StorageLowSpaceNotificationCooldownHours)
                ?? MonitoringOptions.StorageLowSpaceNotificationCooldownHours;
            var clockSkewMonitoringEnabled = GetIntValue(dbContext, AppConfigurationNames.ClockSkewMonitoringEnabled);
            ClockSkewMonitoringEnabled = clockSkewMonitoringEnabled.HasValue
                ? clockSkewMonitoringEnabled.Value != 0
                : MonitoringOptions.ClockSkewMonitoringEnabled;
            ClockSkewCheckIntervalHours =
                GetIntValue(dbContext, AppConfigurationNames.ClockSkewCheckIntervalHours)
                ?? MonitoringOptions.ClockSkewCheckIntervalHours;
            ClockSkewThresholdSeconds =
                GetIntValue(dbContext, AppConfigurationNames.ClockSkewThresholdSeconds)
                ?? MonitoringOptions.ClockSkewThresholdSeconds;
            var clockSkewNtpServer =
                GetStringValue(dbContext, AppConfigurationNames.ClockSkewNtpServer);
            ClockSkewNtpServer = string.IsNullOrWhiteSpace(clockSkewNtpServer)
                ? MonitoringOptions.ClockSkewNtpServer
                : clockSkewNtpServer;

            // 複数キーワード一致時タグマージの全体設定
            var mergeTagsFromAllMatchedRules =
                GetIntValue(dbContext, AppConfigurationNames.MergeTagsFromAllMatchedKeywordRules);
            MergeTagsFromAllMatchedKeywordRules =
                mergeTagsFromAllMatchedRules.HasValue
                    ? mergeTagsFromAllMatchedRules.Value != 0
                    : AutomationOptions.MergeTagsFromAllMatchedKeywordRules;

            var embedProgramImageOnRecord =
                GetIntValue(dbContext, AppConfigurationNames.EmbedProgramImageOnRecord);
            EmbedProgramImageOnRecord = embedProgramImageOnRecord.HasValue && embedProgramImageOnRecord.Value != 0;

            var resumePlaybackAcrossPages =
                GetIntValue(dbContext, AppConfigurationNames.ResumePlaybackAcrossPages);
            ResumePlaybackAcrossPages = !resumePlaybackAcrossPages.HasValue || resumePlaybackAcrossPages.Value != 0;

            // 新リリースチェック間隔（日）
            var releaseCheckIntervalDays =
                GetIntValue(dbContext, AppConfigurationNames.ReleaseCheckIntervalDays);
            ReleaseCheckIntervalDays =
                releaseCheckIntervalDays
                ?? ReleaseOptions.ReleaseCheckIntervalDays;

            // 類似録音抽出ジョブ実行間隔（日）
            var duplicateDetectionIntervalDays =
                GetIntValue(dbContext, AppConfigurationNames.DuplicateDetectionIntervalDays);
            DuplicateDetectionIntervalDays =
                duplicateDetectionIntervalDays
                ?? AutomationOptions.DuplicateDetectionIntervalDays;

            var scheduleDayOfWeek = GetIntValue(dbContext, AppConfigurationNames.DuplicateDetectionScheduleDayOfWeek);
            var scheduleHour = GetIntValue(dbContext, AppConfigurationNames.DuplicateDetectionScheduleHour);
            var scheduleMinute = GetIntValue(dbContext, AppConfigurationNames.DuplicateDetectionScheduleMinute);

            DuplicateDetectionScheduleDayOfWeek = scheduleDayOfWeek is >= 0 and <= 6
                ? scheduleDayOfWeek.Value
                : Math.Clamp(AutomationOptions.DuplicateDetectionScheduleDayOfWeek, 0, 6);
            DuplicateDetectionScheduleHour = scheduleHour is >= 0 and <= 23
                ? scheduleHour.Value
                : Math.Clamp(AutomationOptions.DuplicateDetectionScheduleHour, 0, 23);
            DuplicateDetectionScheduleMinute = scheduleMinute is >= 0 and <= 59
                ? scheduleMinute.Value
                : Math.Clamp(AutomationOptions.DuplicateDetectionScheduleMinute, 0, 59);

            // らじる★らじるエリア設定
            var radiruArea = GetStringValue(dbContext, AppConfigurationNames.RadiruArea) ?? string.Empty;
            RadiruArea = string.IsNullOrEmpty(radiruArea) ? RadiruAreaKind.東京.GetEnumCodeId() : radiruArea;

            // 外部サービス接続用 User-Agent
            var externalServiceUserAgent = GetStringValue(dbContext, AppConfigurationNames.ExternalServiceUserAgent) ?? string.Empty;
            ExternalServiceUserAgent = string.IsNullOrWhiteSpace(externalServiceUserAgent)
                ? ExternalServiceOptions.ExternalServiceUserAgent
                : externalServiceUserAgent;
            RadiruApiMinRequestIntervalMs =
                GetIntValue(dbContext, AppConfigurationNames.RadiruApiMinRequestIntervalMs)
                ?? ExternalServiceOptions.RadiruApiMinRequestIntervalMs;
            RadiruApiRequestJitterMs =
                GetIntValue(dbContext, AppConfigurationNames.RadiruApiRequestJitterMs)
                ?? ExternalServiceOptions.RadiruApiRequestJitterMs;

            // お知らせ通知カテゴリ
            var noticeCategories = GetStringValue(dbContext, AppConfigurationNames.NoticeCategories) ?? string.Empty;
            NoticeCategories = ParseNoticeCategories(noticeCategories, []);

            // 未読バッジ件数に含めるカテゴリ
            var unreadBadgeNoticeCategories = GetStringValue(dbContext, AppConfigurationNames.UnreadBadgeNoticeCategories) ?? string.Empty;
            var defaultUnreadBadgeCategories = Enum.GetValues<NoticeCategory>()
                .Where(x => x != NoticeCategory.Undefined)
                .ToList();
            UnreadBadgeNoticeCategories = ParseNoticeCategories(unreadBadgeNoticeCategories, defaultUnreadBadgeCategories);

            // radikoログイン情報（暗号化）
            var protectedUserId = GetStringValue(dbContext, AppConfigurationNames.RadikoUserIdProtected);
            var protectedPassword = GetStringValue(dbContext, AppConfigurationNames.RadikoPasswordProtected);

            if (!string.IsNullOrWhiteSpace(protectedUserId) && !string.IsNullOrWhiteSpace(protectedPassword))
            {
                try
                {
                    var userId = _dataProtector.Unprotect(protectedUserId);
                    _dataProtector.Unprotect(protectedPassword);

                    RadikoOptions.RadikoUserId = userId;
                    RadikoOptions.RadikoPassword = string.Empty;
                    HasRadikoCredentials = true;
                }
                catch (Exception ex)
                {
                    _logger.ZLogError(ex, $"radikoログイン情報の復号に失敗しました。");
                    HasRadikoCredentials = false;
                }
            }
            else
            {
                HasRadikoCredentials = false;
            }
        }



        /// <summary>
        /// 文字列設定値を取得する
        /// </summary>
        /// <param name="dbContext">DBコンテキスト</param>
        /// <param name="name">設定キー</param>
        /// <returns>設定値</returns>
        private static string? GetStringValue(RadioDbContext dbContext, string name)
        {
            return dbContext.AppConfigurations.FirstOrDefault(r => r.ConfigurationName == name)?.Val1;
        }

        /// <summary>
        /// 数値設定値を取得する
        /// </summary>
        /// <param name="dbContext">DBコンテキスト</param>
        /// <param name="name">設定キー</param>
        /// <returns>設定値</returns>
        private static int? GetIntValue(RadioDbContext dbContext, string name)
        {
            return dbContext.AppConfigurations.FirstOrDefault(r => r.ConfigurationName == name)?.Val2;
        }

        /// <summary>
        /// 文字列設定値を追加または更新する
        /// </summary>
        /// <param name="dbContext">DBコンテキスト</param>
        /// <param name="name">設定キー</param>
        /// <param name="value">設定値</param>
        private static async ValueTask UpsertStringAsync(RadioDbContext dbContext, string name, string value)
        {
            var existing = await dbContext.AppConfigurations.FirstOrDefaultAsync(r => r.ConfigurationName == name);
            if (existing == null)
            {
                await dbContext.AppConfigurations.AddAsync(new AppConfiguration
                {
                    ConfigurationName = name,
                    Val1 = value
                });
                await dbContext.SaveChangesAsync();
                return;
            }

            existing.Val1 = value;
            await dbContext.SaveChangesAsync();
        }

        /// <summary>
        /// 数値設定値を追加または更新する
        /// </summary>
        /// <param name="dbContext">DBコンテキスト</param>
        /// <param name="name">設定キー</param>
        /// <param name="value">設定値</param>
        private static async ValueTask UpsertIntAsync(RadioDbContext dbContext, string name, int value)
        {
            var existing = await dbContext.AppConfigurations.FirstOrDefaultAsync(r => r.ConfigurationName == name);
            if (existing == null)
            {
                await dbContext.AppConfigurations.AddAsync(new AppConfiguration
                {
                    ConfigurationName = name,
                    Val2 = value
                });
                await dbContext.SaveChangesAsync();
                return;
            }

            existing.Val2 = value;
            await dbContext.SaveChangesAsync();
        }

        private IServiceScope CreateDbContextScope(out RadioDbContext context)
        {
            var scope = _serviceProvider.CreateScope();
            context = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            return scope;
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

