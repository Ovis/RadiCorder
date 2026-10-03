using Microsoft.AspNetCore.DataProtection;
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
    }
}
