using System.Collections.Concurrent;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Options;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Services
{
    public interface IAppConfigurationService
    {
        RadikoOptions RadikoOptions { get; }
        string FfmpegExecutablePath { get; }
        int LogRetentionDays { get; }
        int StorageLowSpaceCheckIntervalMinutes { get; }
        int StorageLowSpaceNotificationCooldownHours { get; }
        bool ClockSkewMonitoringEnabled { get; }
        int ClockSkewCheckIntervalHours { get; }
        int ClockSkewThresholdSeconds { get; }
        string ClockSkewNtpServer { get; }
        int RadiruApiMinRequestIntervalMs { get; }
        int RadiruApiRequestJitterMs { get; }
        string ReleaseCheckGitHubOwner { get; }
        string ReleaseCheckGitHubRepository { get; }

        bool IsRadikoPremiumUser { get; }

        /// <summary>
        /// radikoのエリアフリー利用可否
        /// </summary>
        bool IsRadikoAreaFree { get; }
        bool HasRadikoCredentials { get; }

        string RecordFileSaveDir { get; }

        string TemporaryFileSaveDir { get; }

        string? RecordDirectoryRelativePath { get; }

        string? RecordFileNameTemplate { get; }

        TimeSpan RecordStartDuration { get; }

        TimeSpan RecordEndDuration { get; }

        string RadiruArea { get; }
        string ExternalServiceUserAgent { get; }

        string? DiscordWebhookUrl { get; }
        string ExternalImportFileTimeZoneId { get; }
        int StorageLowSpaceThresholdMb { get; }
        bool MergeTagsFromAllMatchedKeywordRules { get; }
        bool EmbedProgramImageOnRecord { get; }
        bool ResumePlaybackAcrossPages { get; }
        int ReleaseCheckIntervalDays { get; }
        int DuplicateDetectionIntervalDays { get; }
        int DuplicateDetectionScheduleDayOfWeek { get; }
        int DuplicateDetectionScheduleHour { get; }
        int DuplicateDetectionScheduleMinute { get; }

        List<NoticeCategory> NoticeCategories { get; }
        List<NoticeCategory> UnreadBadgeNoticeCategories { get; }

        ConcurrentDictionary<string, string> RadikoStationDic { get; }

        ValueTask UpdateRecordDirectoryPathAsync(string recordDirectoryPath);

        ValueTask UpdateRecordFileNameTemplateAsync(string fileNameTemplate);

        ValueTask UpdateDurationAsync(int startDuration, int endDuration);

        ValueTask UpdateRadiruAreaAsync(string area);
        ValueTask UpdateExternalServiceUserAgentAsync(string userAgent);
        ValueTask UpdateRadiruApiRequestSettingsAsync(int minRequestIntervalMs, int requestJitterMs);

        ValueTask UpdateNoticeSettingAsync(string discordWebhookUrl, List<int> selectedNoticeCategories);
        ValueTask UpdateUnreadBadgeNoticeCategoriesAsync(List<int> selectedNoticeCategories);
        ValueTask UpdateExternalImportFileTimeZoneAsync(string timeZoneId);
        ValueTask UpdateStorageLowSpaceThresholdMbAsync(int thresholdMb);
        ValueTask UpdateMonitoringSettingsAsync(int logRetentionDays, int checkIntervalMinutes, int notificationCooldownHours);
        ValueTask UpdateClockSkewMonitoringSettingsAsync(bool enabled, int checkIntervalHours, int thresholdSeconds);
        ValueTask UpdateMergeTagsFromAllMatchedKeywordRulesAsync(bool enabled);
        ValueTask UpdateEmbedProgramImageOnRecordAsync(bool enabled);
        ValueTask UpdateResumePlaybackAcrossPagesAsync(bool enabled);
        ValueTask UpdateReleaseCheckIntervalDaysAsync(int intervalDays);
        ValueTask UpdateDuplicateDetectionIntervalDaysAsync(int intervalDays);
        ValueTask UpdateDuplicateDetectionScheduleAsync(bool enabled, int dayOfWeek, int hour, int minute);

        ValueTask<DateTimeOffset?> GetStorageLowSpaceLastNotifiedAtAsync();

        ValueTask UpdateStorageLowSpaceLastNotifiedAtAsync(DateTimeOffset utcTimestamp);

        ValueTask<DateTimeOffset?> GetReleaseCheckLastCheckedAtAsync();

        ValueTask UpdateReleaseCheckLastCheckedAtAsync(DateTimeOffset utcTimestamp);

        ValueTask<DateTimeOffset?> GetRadiruStationDefinitionLastCheckedAtAsync();

        ValueTask UpdateRadiruStationDefinitionLastCheckedAtAsync(DateTimeOffset utcTimestamp);

        ValueTask<string?> GetReleaseLastNotifiedVersionAsync();

        ValueTask UpdateReleaseLastNotifiedVersionAsync(string version);

        void UpdateRadikoStationDic(List<RadikoStation> stationList);

        string ChooseStationName(RadioServiceKind kind, string stationId);

        void UpdateRadikoPremiumUser(bool isRadikoPremiumUser);

        /// <summary>
        /// radikoのエリアフリー利用可否を更新
        /// </summary>
        /// <param name="isRadikoAreaFree"></param>
        void UpdateRadikoAreaFree(bool isRadikoAreaFree);

        ValueTask UpdateRadikoCredentialsAsync(string userId, string password);

        ValueTask ClearRadikoCredentialsAsync();

        ValueTask<(bool IsSuccess, string UserId, string Password)> TryGetRadikoCredentialsAsync();
    }
}
