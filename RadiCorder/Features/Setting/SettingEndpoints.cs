using Microsoft.AspNetCore.Http.HttpResults;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Extensions;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Logics.RadikoLogic;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.Primitives.DataAnnotations;
using RadiCorder.Logics.Services;
using ZLogger;

namespace RadiCorder.Features.Setting;

/// <summary>
/// 設定関連の Api エンドポイントを提供する。
/// </summary>
public static class SettingEndpoints
{
    /// <summary>
    /// 設定関連エンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapSettingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings").WithTags("SettingsApi");
        group.MapPost("/record-directory", HandleUpdateRecordDirectoryPathAsync)
            .WithName("ApiSettingRecordDirectory")
            .WithSummary("番組の保存先フォルダパスを更新する");
        group.MapPost("/record-filename", HandleUpdateRecordFileNameTemplateAsync)
            .WithName("ApiSettingRecordFilename")
            .WithSummary("番組録音時のファイル名設定を更新する");
        group.MapPost("/duration", HandleUpdateDurationAsync)
            .WithName("ApiSettingDuration")
            .WithSummary("録音時間のマージン設定を更新する");
        group.MapPost("/radiru-area", HandleUpdateRadiruAreaAsync)
            .WithName("ApiSettingRadiruArea")
            .WithSummary("らじるエリア設定を更新する");
        group.MapPost("/notice", HandleUpdateNotificationSettingAsync)
            .WithName("ApiSettingNotice")
            .WithSummary("通知設定を更新する");
        group.MapPost("/unread-badge-notice-categories", HandleUpdateUnreadBadgeNoticeCategoriesAsync)
            .WithName("ApiSettingUnreadBadgeCategories")
            .WithSummary("未読バッジ件数対象カテゴリ設定を更新する");
        group.MapPost("/external-service-user-agent", HandleUpdateExternalServiceUserAgentAsync)
            .WithName("ApiSettingExternalUserAgent")
            .WithSummary("外部サービス接続時のUser-Agentを更新する");
        group.MapPost("/external-service-radiru-request", HandleUpdateRadiruRequestSettingsAsync)
            .WithName("ApiSettingRadiruRequest")
            .WithSummary("らじるAPIアクセス間隔設定を更新する");
        group.MapPost("/radiko-login", HandleUpdateRadikoLoginAsync)
            .WithName("ApiSettingRadikoLogin")
            .WithSummary("radikoログイン情報を更新する");
        group.MapPost("/radiko-logout", HandleClearRadikoLoginAsync)
            .WithName("ApiSettingRadikoLogout")
            .WithSummary("radikoログイン情報を削除する");
        group.MapPost("/radiko-area/refresh", HandleRefreshRadikoAreaAsync)
            .WithName("ApiSettingRadikoAreaRefresh")
            .WithSummary("radikoエリア情報を強制再判定する");
        group.MapPost("/external-import-timezone", HandleUpdateExternalImportTimeZoneAsync)
            .WithName("ApiSettingExternalImportTimeZone")
            .WithSummary("外部取込時のファイル更新日時タイムゾーン設定を更新する");
        group.MapPost("/storage-low-space-threshold", HandleUpdateStorageLowSpaceThresholdAsync)
            .WithName("ApiSettingStorageLowSpaceThreshold")
            .WithSummary("保存先ストレージ空き容量不足通知しきい値を更新する");
        group.MapPost("/monitoring-advanced", HandleUpdateMonitoringAdvancedAsync)
            .WithName("ApiSettingMonitoringAdvanced")
            .WithSummary("監視関連設定を更新する");
        group.MapPost("/clock-skew-monitoring", HandleUpdateClockSkewMonitoringAsync)
            .WithName("ApiSettingClockSkewMonitoring")
            .WithSummary("NTP時刻ずれ監視設定を更新する");
        group.MapPost("/merge-tags-from-matched-rules", HandleUpdateMergeTagsFromMatchedRulesAsync)
            .WithName("ApiSettingMergeTagsFromMatchedRules")
            .WithSummary("複数キーワード一致時のタグ集約付与設定を更新する");
        group.MapPost("/embed-program-image-on-record", HandleUpdateEmbedProgramImageOnRecordAsync)
            .WithName("ApiSettingEmbedProgramImageOnRecord")
            .WithSummary("録音時の番組イメージ埋め込み設定を更新する");
        group.MapPost("/resume-playback-across-pages", HandleUpdateResumePlaybackAcrossPagesAsync)
            .WithName("ApiSettingResumePlaybackAcrossPages")
            .WithSummary("ページ遷移時の再生復帰設定を更新する");
        group.MapPost("/release-check-interval", HandleUpdateReleaseCheckIntervalAsync)
            .WithName("ApiSettingReleaseCheckInterval")
            .WithSummary("新しいリリース確認設定を更新する");
        group.MapPost("/duplicate-detection-interval", HandleUpdateDuplicateDetectionIntervalAsync)
            .WithName("ApiSettingDuplicateDetectionInterval")
            .WithSummary("類似録音抽出の定期実行設定を更新する");
        return endpoints;
    }

    /// <summary>
    /// 番組の保存先フォルダパスを更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateRecordDirectoryPathAsync(
        IAppConfigurationService appConfigurationService,
        UpdateRecordDirectoryPathEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.DirectoryPath) && !entity.DirectoryPath.IsValidRelativePath())
        {
            return TypedResults.BadRequest(ApiResponse.Fail("保存先フォルダパスが不正です。"));
        }

        await appConfigurationService.UpdateRecordDirectoryPathAsync(entity.DirectoryPath);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 番組録音時のファイル名設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateRecordFileNameTemplateAsync(
        IAppConfigurationService appConfigurationService,
        UpdateRecordFileNameTemplateEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.FileNameTemplate) && !entity.FileNameTemplate.IsValidFileName())
        {
            return TypedResults.BadRequest(ApiResponse.Fail("ファイル名テンプレートが不正です。"));
        }

        await appConfigurationService.UpdateRecordFileNameTemplateAsync(entity.FileNameTemplate);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 録音時間のマージン設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateDurationAsync(
        ILogger<SettingEndpointsMarker> logger,
        IAppConfigurationService appConfigurationService,
        ReserveLobLogic reserveLobLogic,
        UpdateDurationEntity entity)
    {
        try
        {
            await appConfigurationService.UpdateDurationAsync(entity.StartDuration ?? 0, entity.EndDuration ?? 0);
            await reserveLobLogic.UpdateReserveDurationAsync();
        }
        catch (Exception e)
        {
            logger.ZLogError(e, $"録音時間のマージン設定でエラーが発生しました。");
            return TypedResults.BadRequest(ApiResponse.Fail("録音時間のマージン設定に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// らじるエリア設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateRadiruAreaAsync(
        ILogger<SettingEndpointsMarker> logger,
        IAppConfigurationService appConfigurationService,
        UpdateRadiruAreaEntity entity)
    {
        if (Enum.GetValues<RadiruAreaKind>().All(r => r.GetEnumCodeId() != entity.RadiruArea))
        {
            logger.ZLogError($"指定されたエリア情報が不正です。");
            return TypedResults.BadRequest(ApiResponse.Fail("指定されたエリア情報が不正です。"));
        }

        try
        {
            await appConfigurationService.UpdateRadiruAreaAsync(entity.RadiruArea);
        }
        catch (Exception e)
        {
            logger.ZLogError(e, $"らじる★らじるのエリア設定でエラーが発生しました。");
            return TypedResults.BadRequest(ApiResponse.Fail("らじる★らじるのエリア設定に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 通知設定を更新する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleUpdateNotificationSettingAsync(
        IAppConfigurationService appConfigurationService,
        UpdateNotificationSettingEntity entity)
    {
        await appConfigurationService.UpdateNoticeSettingAsync(
            discordWebhookUrl: entity.DiscordWebhookUrl,
            selectedNoticeCategories: entity.NotificationCategories);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 未読バッジ件数対象カテゴリ設定を更新する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleUpdateUnreadBadgeNoticeCategoriesAsync(
        IAppConfigurationService appConfigurationService,
        UpdateUnreadBadgeNoticeCategoriesEntity entity)
    {
        await appConfigurationService.UpdateUnreadBadgeNoticeCategoriesAsync(entity.NotificationCategories);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 外部サービス接続時のUser-Agentを更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateExternalServiceUserAgentAsync(
        ILogger<SettingEndpointsMarker> logger,
        IAppConfigurationService appConfigurationService,
        UpdateExternalServiceUserAgentEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.UserAgent))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("User-Agent を入力してください。"));
        }

        if (entity.UserAgent.Length > 1024)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("User-Agent が長すぎます。1024文字以内で入力してください。"));
        }

        try
        {
            await appConfigurationService.UpdateExternalServiceUserAgentAsync(entity.UserAgent.Trim());
        }
        catch (Exception e)
        {
            logger.ZLogError(e, $"外部サービス接続時の User-Agent 設定でエラーが発生しました。");
            return TypedResults.BadRequest(ApiResponse.Fail("接続設定（User-Agent）の保存に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// らじるAPIアクセス間隔設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateRadiruRequestSettingsAsync(
        IAppConfigurationService appConfigurationService,
        UpdateRadiruRequestSettingsEntity entity)
    {
        if (entity.MinRequestIntervalMs < 0 || entity.MinRequestIntervalMs > 60000)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("最小待機時間は 0〜60000 ミリ秒の範囲で指定してください。"));
        }
        if (entity.RequestJitterMs < 0 || entity.RequestJitterMs > 60000)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("ランダム揺らぎは 0〜60000 ミリ秒の範囲で指定してください。"));
        }

        await appConfigurationService.UpdateRadiruApiRequestSettingsAsync(
            entity.MinRequestIntervalMs,
            entity.RequestJitterMs);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// radikoログイン情報を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateRadikoLoginAsync(
        ILogger<SettingEndpointsMarker> logger,
        IAppConfigurationService appConfigurationService,
        ProgramScheduleLobLogic programScheduleLobLogic,
        ProgramUpdateRunner programUpdateRunner,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic,
        UpdateRadikoLoginEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.UserId) || string.IsNullOrWhiteSpace(entity.Password))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("ユーザーIDまたはパスワードが不正です。"));
        }

        var userId = entity.UserId.Trim();
        var password = entity.Password;

        var (isLoginSuccess, _, isPremiumUser, isAreaFree) = await radikoUniqueProcessLogic.TryLoginWithCredentialsAsync(userId, password);
        if (!isLoginSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("radikoログインに失敗しました。メールアドレスまたはパスワードを確認してください。"));
        }

        await appConfigurationService.UpdateRadikoCredentialsAsync(userId, password);
        appConfigurationService.UpdateRadikoPremiumUser(isPremiumUser);
        appConfigurationService.UpdateRadikoAreaFree(isAreaFree);

        if (isAreaFree)
        {
            var hasEnoughProgramData = await programScheduleLobLogic.HasRadikoProgramsForAllStationsThroughAsync();
            if (hasEnoughProgramData)
            {
                return TypedResults.Ok(ApiResponse.Ok("更新しました。エリアフリー会員の番組表データは最新範囲まで取得済みのため、更新処理はスキップしました。"));
            }

            try
            {
                // リクエストスコープ内で実行し、破棄済み DbContext 参照を防ぐ。
                await programUpdateRunner.ExecuteAsync("radiko-login");
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"radikoログイン成功後の番組表即時更新ジョブ起動に失敗しました。");
                return TypedResults.BadRequest(ApiResponse.Fail("radikoログイン情報は保存しましたが、自動予約反映のための更新処理開始に失敗しました。番組表更新を手動実行してください。"));
            }

            return TypedResults.Ok(ApiResponse.Ok("更新しました。エリアフリー会員のため、自動予約反映の番組表更新処理を開始しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// radikoログイン情報を削除する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleClearRadikoLoginAsync(
        ILogger<SettingEndpointsMarker> logger,
        IAppConfigurationService appConfigurationService,
        RadikoUniqueProcessLogic radikoUniqueProcessLogic)
    {
        try
        {
            var (hasCredentials, _, _) = await appConfigurationService.TryGetRadikoCredentialsAsync();
            if (hasCredentials)
            {
                var (isSuccess, session, _, _) = await radikoUniqueProcessLogic.LoginRadikoAsync();
                if (isSuccess)
                {
                    await radikoUniqueProcessLogic.LogoutRadikoAsync(session);
                }
            }
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"radikoログアウトの呼び出しに失敗しました。");
        }

        await appConfigurationService.ClearRadikoCredentialsAsync();
        return TypedResults.Ok(ApiResponse.Ok("削除しました。"));
    }

    /// <summary>
    /// radikoエリア情報を強制再判定する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleRefreshRadikoAreaAsync(
        RadikoUniqueProcessLogic radikoUniqueProcessLogic)
    {
        var (isSuccess, area) = await radikoUniqueProcessLogic.RefreshRadikoAreaCacheAsync();
        if (!isSuccess || string.IsNullOrWhiteSpace(area))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("radikoエリア情報の再判定に失敗しました。"));
        }

        return TypedResults.Ok(ApiResponse.Ok($"radikoエリア情報を再判定しました。（{area}）"));
    }

    /// <summary>
    /// 外部取込時のファイル更新日時タイムゾーン設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateExternalImportTimeZoneAsync(
        IAppConfigurationService appConfigurationService,
        UpdateExternalImportTimeZoneEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.TimeZoneId))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("タイムゾーンIDが未入力です。"));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(entity.TimeZoneId);
        }
        catch
        {
            return TypedResults.BadRequest(ApiResponse.Fail("指定されたタイムゾーンIDは無効です。"));
        }

        await appConfigurationService.UpdateExternalImportFileTimeZoneAsync(entity.TimeZoneId);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 保存先ストレージ空き容量不足通知しきい値（MB）設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateStorageLowSpaceThresholdAsync(
        IAppConfigurationService appConfigurationService,
        UpdateStorageLowSpaceThresholdEntity entity)
    {
        if (entity.ThresholdMb <= 0 || entity.ThresholdMb > int.MaxValue)
        {
            return TypedResults.BadRequest(ApiResponse.Fail($"しきい値は1以上 {int.MaxValue} 以下の数値（MB）で指定してください。"));
        }

        await appConfigurationService.UpdateStorageLowSpaceThresholdMbAsync((int)entity.ThresholdMb);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 監視関連設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateMonitoringAdvancedAsync(
        IAppConfigurationService appConfigurationService,
        UpdateMonitoringAdvancedEntity entity)
    {
        if (entity.LogRetentionDays <= 0 || entity.LogRetentionDays > 3650)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("ログ保持日数は 1〜3650 日の範囲で指定してください。"));
        }
        if (entity.StorageLowSpaceCheckIntervalMinutes <= 0 || entity.StorageLowSpaceCheckIntervalMinutes > 1440)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("空き容量チェック間隔は 1〜1440 分の範囲で指定してください。"));
        }
        if (entity.StorageLowSpaceNotificationCooldownHours <= 0 || entity.StorageLowSpaceNotificationCooldownHours > 168)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("通知クールダウンは 1〜168 時間の範囲で指定してください。"));
        }

        await appConfigurationService.UpdateMonitoringSettingsAsync(
            entity.LogRetentionDays,
            entity.StorageLowSpaceCheckIntervalMinutes,
            entity.StorageLowSpaceNotificationCooldownHours);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// NTP時刻ずれ監視設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateClockSkewMonitoringAsync(
        IAppConfigurationService appConfigurationService,
        UpdateClockSkewMonitoringEntity entity)
    {
        if (entity.CheckIntervalHours is < 1 or > 168)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("監視間隔は 1〜168 時間の範囲で指定してください。"));
        }
        if (entity.ThresholdSeconds is < 1 or > 600)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("ずれしきい値は 1〜600 秒の範囲で指定してください。"));
        }

        await appConfigurationService.UpdateClockSkewMonitoringSettingsAsync(
            entity.Enabled,
            entity.CheckIntervalHours,
            entity.ThresholdSeconds);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 複数キーワード一致時のタグ集約付与設定を更新する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleUpdateMergeTagsFromMatchedRulesAsync(
        IAppConfigurationService appConfigurationService,
        UpdateMergeTagsFromMatchedRulesEntity entity)
    {
        await appConfigurationService.UpdateMergeTagsFromAllMatchedKeywordRulesAsync(entity.Enabled);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 録音時の番組イメージ埋め込み設定を更新する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleUpdateEmbedProgramImageOnRecordAsync(
        IAppConfigurationService appConfigurationService,
        UpdateEmbedProgramImageOnRecordEntity entity)
    {
        await appConfigurationService.UpdateEmbedProgramImageOnRecordAsync(entity.Enabled);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// ページ遷移時の再生復帰設定を更新する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleUpdateResumePlaybackAcrossPagesAsync(
        IAppConfigurationService appConfigurationService,
        UpdateResumePlaybackAcrossPagesEntity entity)
    {
        await appConfigurationService.UpdateResumePlaybackAcrossPagesAsync(entity.Enabled);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 新しいリリース確認設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateReleaseCheckIntervalAsync(
        IAppConfigurationService appConfigurationService,
        UpdateReleaseCheckIntervalEntity entity)
    {
        var allowedIntervals = new[] { 0, 1, 7, 30 };
        if (!allowedIntervals.Contains(entity.IntervalDays))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("チェック間隔は 0, 1, 7, 30 のいずれかで指定してください。"));
        }

        await appConfigurationService.UpdateReleaseCheckIntervalDaysAsync(entity.IntervalDays);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 類似録音抽出の定期実行設定を更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateDuplicateDetectionIntervalAsync(
        IAppConfigurationService appConfigurationService,
        UpdateDuplicateDetectionIntervalEntity entity)
    {
        if (entity.DayOfWeek is < 0 or > 6)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("曜日は 0(日曜) 〜 6(土曜) の範囲で指定してください。"));
        }
        if (entity.Hour is < 0 or > 23)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("時は 0〜23 の範囲で指定してください。"));
        }
        if (entity.Minute is < 0 or > 59)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("分は 0〜59 の範囲で指定してください。"));
        }

        await appConfigurationService.UpdateDuplicateDetectionScheduleAsync(
            entity.Enabled,
            entity.DayOfWeek,
            entity.Hour,
            entity.Minute);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 保存先フォルダ更新要求。
    /// </summary>
    public sealed class UpdateRecordDirectoryPathEntity
    {
        public string DirectoryPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// ファイル名テンプレート更新要求。
    /// </summary>
    public sealed class UpdateRecordFileNameTemplateEntity
    {
        public string FileNameTemplate { get; set; } = string.Empty;
    }

    /// <summary>
    /// 録音マージン更新要求。
    /// </summary>
    public sealed class UpdateDurationEntity
    {
        public int? StartDuration { get; set; }
        public int? EndDuration { get; set; }
    }

    /// <summary>
    /// らじるエリア更新要求。
    /// </summary>
    public sealed class UpdateRadiruAreaEntity
    {
        public string RadiruArea { get; set; } = string.Empty;
    }

    /// <summary>
    /// 外部サービスUser-Agent更新要求。
    /// </summary>
    public sealed class UpdateExternalServiceUserAgentEntity
    {
        public string UserAgent { get; set; } = string.Empty;
    }

    /// <summary>
    /// 通知設定更新要求。
    /// </summary>
    public sealed class UpdateNotificationSettingEntity
    {
        public string DiscordWebhookUrl { get; set; } = string.Empty;
        public List<int> NotificationCategories { get; set; } = new();
    }

    /// <summary>
    /// 未読バッジカテゴリ更新要求。
    /// </summary>
    public sealed class UpdateUnreadBadgeNoticeCategoriesEntity
    {
        public List<int> NotificationCategories { get; set; } = new();
    }

    /// <summary>
    /// 外部取込タイムゾーン更新要求。
    /// </summary>
    public sealed class UpdateExternalImportTimeZoneEntity
    {
        public string TimeZoneId { get; set; } = string.Empty;
    }

    /// <summary>
    /// ストレージ空き容量しきい値更新要求。
    /// </summary>
    public sealed class UpdateStorageLowSpaceThresholdEntity
    {
        public long ThresholdMb { get; set; }
    }

    /// <summary>
    /// タグ集約設定更新要求。
    /// </summary>
    public sealed class UpdateMergeTagsFromMatchedRulesEntity
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 番組画像埋め込み設定更新要求。
    /// </summary>
    public sealed class UpdateEmbedProgramImageOnRecordEntity
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// らじるリクエスト設定更新要求。
    /// </summary>
    public sealed class UpdateRadiruRequestSettingsEntity
    {
        public int MinRequestIntervalMs { get; set; }
        public int RequestJitterMs { get; set; }
    }

    /// <summary>
    /// 再生復帰設定更新要求。
    /// </summary>
    public sealed class UpdateResumePlaybackAcrossPagesEntity
    {
        public bool Enabled { get; set; }
    }

    /// <summary>
    /// 監視詳細設定更新要求。
    /// </summary>
    public sealed class UpdateMonitoringAdvancedEntity
    {
        public int LogRetentionDays { get; set; }
        public int StorageLowSpaceCheckIntervalMinutes { get; set; }
        public int StorageLowSpaceNotificationCooldownHours { get; set; }
    }

    /// <summary>
    /// NTP時刻ずれ監視設定更新要求。
    /// </summary>
    public sealed class UpdateClockSkewMonitoringEntity
    {
        public bool Enabled { get; set; }
        public int CheckIntervalHours { get; set; }
        public int ThresholdSeconds { get; set; }
    }

    /// <summary>
    /// リリース確認間隔更新要求。
    /// </summary>
    public sealed class UpdateReleaseCheckIntervalEntity
    {
        public int IntervalDays { get; set; }
    }

    /// <summary>
    /// 類似録音抽出スケジュール更新要求。
    /// </summary>
    public sealed class UpdateDuplicateDetectionIntervalEntity
    {
        public bool Enabled { get; set; }
        public int DayOfWeek { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
    }

    /// <summary>
    /// SettingEndpoints 用のロガーカテゴリ型。
    /// </summary>
    private sealed class SettingEndpointsMarker;
}



