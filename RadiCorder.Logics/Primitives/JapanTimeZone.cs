namespace RadiCorder.Logics.Primitives;

/// <summary>
/// 日本標準時 (JST) の TimeZoneInfo を環境差異を吸収して解決する。
/// </summary>
public static class JapanTimeZone
{
    private static readonly Lazy<TimeZoneInfo> Cached = new(ResolveInternal);

    public static TimeZoneInfo Resolve()
    {
        return Cached.Value;
    }

    private static TimeZoneInfo ResolveInternal()
    {
        // Windows / IANA など実行環境で使えるIDが異なるため順番に試す
        var candidateIds = new[]
        {
            "Tokyo Standard Time",
            "Asia/Tokyo",
            "Japan"
        };

        foreach (var id in candidateIds)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // 最後のフォールバック: UTC+09:00 固定 (JSTはDSTなし)
        return TimeZoneInfo.CreateCustomTimeZone(
            id: "JST-Fallback",
            baseUtcOffset: TimeSpan.FromHours(9),
            displayName: "(UTC+09:00) Osaka, Sapporo, Tokyo",
            standardDisplayName: "Japan Standard Time");
    }
}
