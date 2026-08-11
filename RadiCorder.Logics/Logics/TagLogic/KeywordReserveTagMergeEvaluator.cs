using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Logics.TagLogic;

/// <summary>
/// キーワード予約ルール群から、実際にタグ付与対象とするルールを決定する
/// </summary>
public static class KeywordReserveTagMergeEvaluator
{
    public static List<Ulid> ResolveTargetReserveIds(
        IReadOnlyList<(Ulid ReserveId, int SortOrder, KeywordReserveTagMergeBehavior MergeTagBehavior)> reserveSettings,
        bool globalMergeDefault,
        Ulid? primaryReserveId)
    {
        if (reserveSettings.Count == 0)
        {
            return [];
        }

        var ordered = reserveSettings
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.ReserveId)
            .ToList();

        var orderedReserveIds = ordered.Select(x => x.ReserveId).ToList();
        var effectivePrimaryReserveId = primaryReserveId != null && orderedReserveIds.Contains(primaryReserveId.Value)
            ? primaryReserveId.Value
            : orderedReserveIds[0];

        var isMultiMatched = ordered.Count > 1;

        return ordered
            .Where(x => ShouldIncludeReserve(
                x.ReserveId,
                x.MergeTagBehavior,
                isMultiMatched,
                globalMergeDefault,
                effectivePrimaryReserveId))
            .Select(x => x.ReserveId)
            .ToList();
    }

    private static bool ShouldIncludeReserve(
        Ulid reserveId,
        KeywordReserveTagMergeBehavior behavior,
        bool isMultiMatched,
        bool globalMergeDefault,
        Ulid primaryReserveId)
    {
        return behavior switch
        {
            KeywordReserveTagMergeBehavior.ForceMerge => true,
            KeywordReserveTagMergeBehavior.ForceSingle => !isMultiMatched,
            _ => globalMergeDefault || reserveId == primaryReserveId
        };
    }
}
