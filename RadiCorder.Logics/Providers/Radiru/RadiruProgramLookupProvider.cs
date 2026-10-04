using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Providers.Radiru;

public class RadiruProgramLookupProvider(IProgramScheduleRepository repository, IEntryMapper mapper) : IProgramLookupProvider
{
    public RadioServiceKind ServiceKind => RadioServiceKind.Radiru;
    public async ValueTask<RadioProgramEntry?> GetAsync(string programId)
    {
        var program = await repository.GetRadiruProgramByIdAsync(programId);
        return program == null ? null : mapper.ToRadioProgramEntry(program);
    }
}
