using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Logics.ProgramScheduleLogic;

/// <summary>
/// サービス別の検索結果を、既存の優先順と件数制限で統合する。
/// </summary>
public static class ProgramSearchResultBuilder
{
    public static List<ProgramForApiEntry> Build(
        IReadOnlyList<RadikoProgram> radikoResults,
        IReadOnlyList<NhkRadiruProgram> radiruResults,
        KeywordReserveOrderKind orderKind,
        IEntryMapper entryMapper)
    {
        var sortedRadiko = SortSearchResults(
            radikoResults,
            orderKind,
            x => x.StartTime,
            x => x.EndTime,
            x => x.Title);
        var sortedRadiru = SortSearchResults(
            radiruResults,
            orderKind,
            x => x.StartTime,
            x => x.EndTime,
            x => x.Title);

        var result = orderKind switch
        {
            KeywordReserveOrderKind.ProgramStartDateTimeAsc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => left.StartTime.CompareTo(right.StartTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramStartDateTimeDesc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => right.StartTime.CompareTo(left.StartTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramEndDateTimeAsc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => left.EndTime.CompareTo(right.EndTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramEndDateTimeDesc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => right.EndTime.CompareTo(left.EndTime),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramNameAsc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => StringComparer.Ordinal.Compare(left.Title, right.Title),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            KeywordReserveOrderKind.ProgramNameDesc => MergeSortedSearchResults(
                sortedRadiko,
                sortedRadiru,
                (left, right) => StringComparer.Ordinal.Compare(right.Title, left.Title),
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right)),
            _ => TakeConcatenatedSearchResults(
                radikoResults,
                radiruResults,
                left => entryMapper.ToRadikoProgramForApiEntry(left),
                right => entryMapper.ToRadiruProgramForApiEntry(right))
        };

        return result;
    }

    private static List<T> SortSearchResults<T>(
        IEnumerable<T> source,
        KeywordReserveOrderKind orderKind,
        Func<T, DateTimeOffset> startSelector,
        Func<T, DateTimeOffset> endSelector,
        Func<T, string?> titleSelector)
    {
        return orderKind switch
        {
            KeywordReserveOrderKind.ProgramStartDateTimeAsc => source.OrderBy(startSelector).ToList(),
            KeywordReserveOrderKind.ProgramStartDateTimeDesc => source.OrderByDescending(startSelector).ToList(),
            KeywordReserveOrderKind.ProgramEndDateTimeAsc => source.OrderBy(endSelector).ToList(),
            KeywordReserveOrderKind.ProgramEndDateTimeDesc => source.OrderByDescending(endSelector).ToList(),
            KeywordReserveOrderKind.ProgramNameAsc => source.OrderBy(x => titleSelector(x) ?? string.Empty, StringComparer.Ordinal).ToList(),
            KeywordReserveOrderKind.ProgramNameDesc => source.OrderByDescending(x => titleSelector(x) ?? string.Empty, StringComparer.Ordinal).ToList(),
            _ => source.OrderBy(startSelector).ToList()
        };
    }

    private static List<ProgramForApiEntry> MergeSortedSearchResults<TLeft, TRight>(
        IReadOnlyList<TLeft> left,
        IReadOnlyList<TRight> right,
        Func<TLeft, TRight, int> compare,
        Func<TLeft, ProgramForApiEntry> leftMapper,
        Func<TRight, ProgramForApiEntry> rightMapper,
        int take = 100)
    {
        var result = new List<ProgramForApiEntry>(Math.Min(take, left.Count + right.Count));
        var leftIndex = 0;
        var rightIndex = 0;

        while (result.Count < take && (leftIndex < left.Count || rightIndex < right.Count))
        {
            if (leftIndex >= left.Count)
            {
                result.Add(rightMapper(right[rightIndex++]));
                continue;
            }

            if (rightIndex >= right.Count)
            {
                result.Add(leftMapper(left[leftIndex++]));
                continue;
            }

            if (compare(left[leftIndex], right[rightIndex]) <= 0)
            {
                result.Add(leftMapper(left[leftIndex++]));
            }
            else
            {
                result.Add(rightMapper(right[rightIndex++]));
            }
        }

        return result;
    }

    private static List<ProgramForApiEntry> TakeConcatenatedSearchResults<TLeft, TRight>(
        IReadOnlyList<TLeft> left,
        IReadOnlyList<TRight> right,
        Func<TLeft, ProgramForApiEntry> leftMapper,
        Func<TRight, ProgramForApiEntry> rightMapper,
        int take = 100)
    {
        var result = new List<ProgramForApiEntry>(Math.Min(take, left.Count + right.Count));

        foreach (var item in left)
        {
            if (result.Count >= take)
            {
                return result;
            }

            result.Add(leftMapper(item));
        }

        foreach (var item in right)
        {
            if (result.Count >= take)
            {
                return result;
            }

            result.Add(rightMapper(item));
        }

        return result;
    }
}
