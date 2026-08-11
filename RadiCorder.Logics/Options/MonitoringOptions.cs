namespace RadiCorder.Logics.Options;

/// <summary>
/// ログ保守やストレージ監視など運用監視系の設定。
/// </summary>
public class MonitoringOptions
{
    /// <summary>
    /// ログファイルの保持日数。
    /// </summary>
    public int LogRetentionDays { get; set; } = 100;

    /// <summary>
    /// 空き容量不足通知のしきい値（MB）。
    /// </summary>
    public int StorageLowSpaceThresholdMb { get; set; } = 1024;

    /// <summary>
    /// 空き容量監視の実行間隔（分）。
    /// </summary>
    public int StorageLowSpaceCheckIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// 空き容量不足通知のクールダウン時間（時間）。
    /// </summary>
    public int StorageLowSpaceNotificationCooldownHours { get; set; } = 24;

    /// <summary>
    /// NTP時刻ずれ監視を有効にするか。
    /// </summary>
    public bool ClockSkewMonitoringEnabled { get; set; } = false;

    /// <summary>
    /// NTP時刻ずれ監視の実行間隔（時間）。
    /// </summary>
    public int ClockSkewCheckIntervalHours { get; set; } = 6;

    /// <summary>
    /// 時刻ずれとみなすしきい値（秒）。
    /// </summary>
    public int ClockSkewThresholdSeconds { get; set; } = 10;

    /// <summary>
    /// 時刻同期に利用するNTPサーバー名。
    /// </summary>
    public string ClockSkewNtpServer { get; set; } = "ntp.nict.jp";
}

