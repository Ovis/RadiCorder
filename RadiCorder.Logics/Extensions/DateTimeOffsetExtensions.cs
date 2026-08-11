using System.Globalization;
using RadiCorder.Logics.Primitives;

namespace RadiCorder.Logics.Extensions;

public static class DateTimeOffsetExtensions
{
    public static readonly TimeZoneInfo TokyoStandardTimeTz = JapanTimeZone.Resolve();

    public static DateTimeOffset ToNormalizedByTz(this DateTimeOffset dateTime)
    {
        return TimeZoneInfo.ConvertTime(dateTime, TokyoStandardTimeTz);
    }

    /// <summary>
    /// yyyyMMddHHmmss形式の文字列を日本時間に変換
    /// </summary>
    /// <param name="dateTimeString"></param>
    /// <returns></returns>
    public static DateTimeOffset ToJapaneseDateTime(this string dateTimeString)
    {
        var dateTime = DateTime.ParseExact(dateTimeString, "yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        return new DateTimeOffset(dateTime, TimeSpan.FromHours(9));
    }

    /// <summary>
    /// UTCからJSTに変換
    /// </summary>
    /// <param name="dateTime"></param>
    /// <returns></returns>
    public static DateTimeOffset UtcToJst(this DateTime dateTime)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.Zero).ToNormalizedByTz();
    }

    /// <summary>
    /// JSTのDateTimeに変換
    /// </summary>
    /// <param name="dateTime"></param>
    /// <returns></returns>
    public static DateTimeOffset JstTimeOffset(this DateTime dateTime)
    {
        return TimeZoneInfo.ConvertTime(dateTime, TokyoStandardTimeTz);
    }

    /// <summary>
    /// 日本時間のDateTimeに変換
    /// </summary>
    /// <param name="dateTimeOffset"></param>
    /// <returns></returns>
    public static DateTime ToJapanDateTime(this DateTimeOffset dateTimeOffset)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(dateTimeOffset.UtcDateTime, TokyoStandardTimeTz);
    }
}
