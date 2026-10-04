using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.ProgramSchedule;

/// <summary>
/// 既存Web DTOから独立して、サービスごとの選択局を指定する内部要求。
/// </summary>
public record ProgramSearchRequest(ProgramSearchEntity Filters, IReadOnlyDictionary<RadioServiceKind, IReadOnlyList<string>> Stations);
