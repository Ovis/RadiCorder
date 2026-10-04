using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.ProgramSchedule;

/// <summary>
/// 公開番組IDを変えず、配信サービスを含めて番組を識別する。
/// </summary>
public readonly record struct ProgramKey(RadioServiceKind ServiceKind, string ProgramId);
