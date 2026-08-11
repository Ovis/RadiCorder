namespace RadiCorder.Logics.RdbContext;

/// <summary>
/// AppConfigurationのキー名定義
/// </summary>
public static class AppConfigurationNames
{
    /// <summary>
    /// 録音フォルダ相対パス
    /// </summary>
    public const string RecordDirectoryPath = "RecordDirectoryPath";

    /// <summary>
    /// 録音ファイル名テンプレート
    /// </summary>
    public const string RecordFileNameTemplate = "RecordFileNameTemplate";

    /// <summary>
    /// 録音開始前ディレイ（秒）
    /// </summary>
    public const string RecordStartDuration = "RecordStartDuration";

    /// <summary>
    /// 録音終了後ディレイ（秒）
    /// </summary>
    public const string RecordEndDuration = "RecordEndDuration";

    /// <summary>
    /// らじる★らじるエリア
    /// </summary>
    public const string RadiruArea = "RadiruArea";

    /// <summary>
    /// 外部サービス接続時に利用するUser-Agent
    /// </summary>
    public const string ExternalServiceUserAgent = "ExternalServiceUserAgent";

    /// <summary>
    /// Discord Webhook URL
    /// </summary>
    public const string DiscordWebhookUrl = "DiscordWebhookUrl";

    /// <summary>
    /// 通知対象カテゴリ
    /// </summary>
    public const string NoticeCategories = "NoticeCategories";

    /// <summary>
    /// 未読バッジ件数に含めるカテゴリ
    /// </summary>
    public const string UnreadBadgeNoticeCategories = "UnreadBadgeNoticeCategories";

    /// <summary>
    /// 番組表の最終更新日時
    /// </summary>
    public const string LastUpdatedProgram = "LastUpdatedProgram";

    /// <summary>
    /// らじる★らじる放送局定義の最終確認時刻（UTC, ISO8601）
    /// </summary>
    public const string RadiruStationDefinitionLastCheckedAtUtc = "RadiruStationDefinitionLastCheckedAtUtc";

    /// <summary>
    /// 外部取込時のファイル更新日時タイムゾーン
    /// </summary>
    public const string ExternalImportFileTimeZoneId = "ExternalImportFileTimeZoneId";

    /// <summary>
    /// radikoログインユーザーID（暗号化済み）
    /// </summary>
    public const string RadikoUserIdProtected = "RadikoUserIdProtected";

    /// <summary>
    /// radikoログインパスワード（暗号化済み）
    /// </summary>
    public const string RadikoPasswordProtected = "RadikoPasswordProtected";

    /// <summary>
    /// 保存先ストレージ空き容量不足通知の最終通知時刻（UTC, ISO8601）
    /// </summary>
    public const string StorageLowSpaceLastNotifiedAtUtc = "StorageLowSpaceLastNotifiedAtUtc";

    /// <summary>
    /// 保存先ストレージ空き容量不足通知のしきい値（MB）
    /// </summary>
    public const string StorageLowSpaceThresholdMb = "StorageLowSpaceThresholdMb";

    /// <summary>
    /// ログファイル保持日数
    /// </summary>
    public const string LogRetentionDays = "LogRetentionDays";

    /// <summary>
    /// ストレージ空き容量監視の実行間隔（分）
    /// </summary>
    public const string StorageLowSpaceCheckIntervalMinutes = "StorageLowSpaceCheckIntervalMinutes";

    /// <summary>
    /// ストレージ空き容量不足通知のクールダウン時間（時間）
    /// </summary>
    public const string StorageLowSpaceNotificationCooldownHours = "StorageLowSpaceNotificationCooldownHours";

    /// <summary>
    /// NTP時刻ずれ監視の有効/無効
    /// </summary>
    public const string ClockSkewMonitoringEnabled = "ClockSkewMonitoringEnabled";

    /// <summary>
    /// NTP時刻ずれ監視の実行間隔（時間）
    /// </summary>
    public const string ClockSkewCheckIntervalHours = "ClockSkewCheckIntervalHours";

    /// <summary>
    /// NTP時刻ずれしきい値（秒）
    /// </summary>
    public const string ClockSkewThresholdSeconds = "ClockSkewThresholdSeconds";

    /// <summary>
    /// 時刻同期に利用するNTPサーバー名
    /// </summary>
    public const string ClockSkewNtpServer = "ClockSkewNtpServer";

    /// <summary>
    /// らじる★らじる番組表API連続アクセス時の最小待機時間（ミリ秒）
    /// </summary>
    public const string RadiruApiMinRequestIntervalMs = "RadiruApiMinRequestIntervalMs";

    /// <summary>
    /// らじる★らじる番組表APIアクセス時のランダム揺らぎ（ミリ秒）
    /// </summary>
    public const string RadiruApiRequestJitterMs = "RadiruApiRequestJitterMs";

    /// <summary>
    /// 複数キーワード一致時にタグを集約して付与するかどうか
    /// </summary>
    public const string MergeTagsFromAllMatchedKeywordRules = "MergeTagsFromAllMatchedKeywordRules";

    /// <summary>
    /// 録音時に番組イメージをカバーアートとして埋め込むかどうか
    /// </summary>
    public const string EmbedProgramImageOnRecord = "EmbedProgramImageOnRecord";

    /// <summary>
    /// ページ遷移時に再生状態を復帰するかどうか
    /// </summary>
    public const string ResumePlaybackAcrossPages = "ResumePlaybackAcrossPages";

    /// <summary>
    /// 新リリースチェック間隔（日）。0以下の場合は無効。
    /// </summary>
    public const string ReleaseCheckIntervalDays = "ReleaseCheckIntervalDays";

    /// <summary>
    /// 新リリースチェックの最終実行時刻（UTC, ISO8601）
    /// </summary>
    public const string ReleaseCheckLastCheckedAtUtc = "ReleaseCheckLastCheckedAtUtc";

    /// <summary>
    /// 新リリース通知済みの最新バージョン
    /// </summary>
    public const string ReleaseLastNotifiedVersion = "ReleaseLastNotifiedVersion";

    /// <summary>
    /// 類似録音抽出ジョブの実行間隔（日）。0以下の場合は無効。
    /// </summary>
    public const string DuplicateDetectionIntervalDays = "DuplicateDetectionIntervalDays";

    /// <summary>
    /// 類似録音抽出の週次実行曜日。0=日曜, 1=月曜, ... 6=土曜。
    /// </summary>
    public const string DuplicateDetectionScheduleDayOfWeek = "DuplicateDetectionScheduleDayOfWeek";

    /// <summary>
    /// 類似録音抽出の週次実行時刻（時）。
    /// </summary>
    public const string DuplicateDetectionScheduleHour = "DuplicateDetectionScheduleHour";

    /// <summary>
    /// 類似録音抽出の週次実行時刻（分）。
    /// </summary>
    public const string DuplicateDetectionScheduleMinute = "DuplicateDetectionScheduleMinute";
}
