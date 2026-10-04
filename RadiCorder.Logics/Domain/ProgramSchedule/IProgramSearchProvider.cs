using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.ProgramSchedule;

/// <summary>
/// 配信サービス固有の検索・利用可能局の判断を提供する。
/// </summary>
public interface IProgramSearchProvider
{
    RadioServiceKind ServiceKind { get; }
    ValueTask<List<ProgramForApiEntry>> SearchAsync(ProgramSearchEntity filters, IReadOnlyList<string> stationIds);
}
