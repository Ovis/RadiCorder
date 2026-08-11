namespace RadiCorder.Logics.Options;

/// <summary>
/// 自動処理（タグ付与・定期抽出）系の設定。
/// </summary>
public class AutomationOptions
{
    /// <summary>
    /// 1つの番組が複数キーワード予約ルールに一致した場合に、タグを統合付与するか。
    /// </summary>
    public bool MergeTagsFromAllMatchedKeywordRules { get; set; } = false;

    /// <summary>
    /// 同一番組候補チェックの定期実行間隔（日）。0以下で無効。
    /// </summary>
    public int DuplicateDetectionIntervalDays { get; set; } = 0;

    /// <summary>
    /// 同一番組候補チェックの週次実行曜日。0=日曜, 1=月曜, ... 6=土曜。
    /// </summary>
    public int DuplicateDetectionScheduleDayOfWeek { get; set; } = 0;

    /// <summary>
    /// 同一番組候補チェックの週次実行時刻（時）。
    /// </summary>
    public int DuplicateDetectionScheduleHour { get; set; } = 3;

    /// <summary>
    /// 同一番組候補チェックの週次実行時刻（分）。
    /// </summary>
    public int DuplicateDetectionScheduleMinute { get; set; } = 0;
}

