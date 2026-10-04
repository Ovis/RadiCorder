using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Mappers;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Providers.Radiko;

public class RadikoProgramLookupProvider(IProgramScheduleRepository repository, IEntryMapper mapper) : IProgramLookupProvider
{
    public RadioServiceKind ServiceKind => RadioServiceKind.Radiko;
    public async ValueTask<RadioProgramEntry?> GetAsync(string programId)
    {
        var program = await repository.GetRadikoProgramByIdAsync(programId);
        return program == null ? null : mapper.ToRadioProgramEntry(program);
    }
}
