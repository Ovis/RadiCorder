using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.ProgramSchedule;

/// <summary>
/// 配信サービスの番組識別子を、予約・詳細表示に必要な情報へ解決する。
/// </summary>
public interface IProgramLookupProvider
{
    RadioServiceKind ServiceKind { get; }
    ValueTask<RadioProgramEntry?> GetAsync(string programId);
}
