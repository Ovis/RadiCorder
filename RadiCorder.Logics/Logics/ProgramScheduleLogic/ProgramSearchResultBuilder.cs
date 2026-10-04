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
        => Build(radikoResults.Select(entryMapper.ToRadikoProgramForApiEntry)
            .Concat(radiruResults.Select(entryMapper.ToRadiruProgramForApiEntry)), orderKind);

    /// <summary>
    /// 任意のサービスの結果を安定ソートし、既存の100件上限を適用する。
    /// </summary>
    public static List<ProgramForApiEntry> Build(IEnumerable<ProgramForApiEntry> programs, KeywordReserveOrderKind orderKind)
    {
        var sorted = orderKind switch
        {
            KeywordReserveOrderKind.ProgramStartDateTimeAsc => programs.OrderBy(x => x.StartTime),
            KeywordReserveOrderKind.ProgramStartDateTimeDesc => programs.OrderByDescending(x => x.StartTime),
            KeywordReserveOrderKind.ProgramEndDateTimeAsc => programs.OrderBy(x => x.EndTime),
            KeywordReserveOrderKind.ProgramEndDateTimeDesc => programs.OrderByDescending(x => x.EndTime),
            KeywordReserveOrderKind.ProgramNameAsc => programs.OrderBy(x => x.Title ?? string.Empty, StringComparer.Ordinal),
            KeywordReserveOrderKind.ProgramNameDesc => programs.OrderByDescending(x => x.Title ?? string.Empty, StringComparer.Ordinal),
            _ => programs
        };
        return sorted.Take(100).ToList();
    }

}
