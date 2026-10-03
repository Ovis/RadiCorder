using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.Models.Radiko;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Domain.Reserve;

/// <summary>
/// キーワード予約の優先順位と録音種別の判断を提供する。
/// </summary>
public static class KeywordReservationPolicy
{
    public static bool IsHigherPriority(KeywordReserve candidate, KeywordReserve current) =>
        candidate.SortOrder != current.SortOrder
            ? candidate.SortOrder < current.SortOrder
            : candidate.Id.CompareTo(current.Id) < 0;

    public static KeywordReserveTagMergeBehavior NormalizeMergeTagBehavior(KeywordReserveTagMergeBehavior behavior)
    {
        return Enum.IsDefined(typeof(KeywordReserveTagMergeBehavior), behavior)
            ? behavior
            : KeywordReserveTagMergeBehavior.Default;
    }

    public static KeywordReserve? ResolvePrimaryKeywordReserve(IEnumerable<KeywordReserve> reserves)
    {
        return reserves
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .FirstOrDefault();
    }

    public static RecordingType ResolveKeywordReserveRecordingType(RadioProgramEntry program)
    {
        return program.AvailabilityTimeFree is AvailabilityTimeFree.Available or AvailabilityTimeFree.PartiallyAvailable
            ? RecordingType.TimeFree
            : RecordingType.RealTime;
    }
}
