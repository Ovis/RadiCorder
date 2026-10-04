using RadiCorder.Logics.Models;

namespace RadiCorder.Logics.Providers;

internal static class ProgramSearchFilters
{
    public static ProgramSearchEntity Copy(ProgramSearchEntity filters) => new()
    {
        ServiceKind = filters.ServiceKind,
        Keyword = filters.Keyword,
        ExcludedKeyword = filters.ExcludedKeyword,
        SearchTitleOnly = filters.SearchTitleOnly,
        SearchTitleOnlyExcludedKeyword = filters.SearchTitleOnlyExcludedKeyword,
        SelectedDaysOfWeek = filters.SelectedDaysOfWeek.ToList(),
        StartTime = filters.StartTime,
        EndTime = filters.EndTime,
        IncludeHistoricalPrograms = filters.IncludeHistoricalPrograms,
        RecordableOnly = filters.RecordableOnly,
        OrderKindString = filters.OrderKindString
    };
}
