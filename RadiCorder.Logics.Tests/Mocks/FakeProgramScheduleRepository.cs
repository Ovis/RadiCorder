using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.NhkRadiru;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// 番組表リポジトリのテスト用スタブ
/// </summary>
public class FakeProgramScheduleRepository : IProgramScheduleRepository
{
    public RadikoProgram? RadikoProgramById { get; set; }

    public ValueTask<List<RadikoProgram>> GetRadikoNowOnAirAsync(DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<RadikoProgram>());

    public ValueTask<List<NhkRadiruProgram>> GetRadiruNowOnAirAsync(DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<NhkRadiruProgram>());

    public ValueTask<List<RadikoProgram>> GetRadikoProgramsAsync(DateOnly date, string stationId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<RadikoProgram>());

    public ValueTask<RadikoProgram?> GetRadikoProgramByIdAsync(string programId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(RadikoProgramById);

    public ValueTask<List<string>> GetRadikoStationIdsAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<string>());

    public ValueTask AddRadikoProgramsIfMissingAsync(IEnumerable<RadikoProgram> programs, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<bool> HasRadikoProgramsForAllStationsThroughAsync(DateOnly targetDate, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(false);

    public ValueTask<List<RadikoProgram>> SearchRadikoProgramsAsync(ProgramSearchEntity searchEntity, DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<RadikoProgram>());

    public ValueTask DeleteOldRadikoProgramsAsync(DateOnly deleteDate, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<List<NhkRadiruProgram>> GetRadiruProgramsAsync(DateOnly date, string areaId, string stationId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<NhkRadiruProgram>());

    public ValueTask<NhkRadiruProgram?> GetRadiruProgramByIdAsync(string programId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<NhkRadiruProgram?>(null);

    public ValueTask UpsertRadiruProgramsAsync(IEnumerable<NhkRadiruProgram> programs, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<List<NhkRadiruProgram>> SearchRadiruProgramsAsync(ProgramSearchEntity searchEntity, DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<NhkRadiruProgram>());

    public ValueTask DeleteOldRadiruProgramsAsync(DateOnly deleteDate, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<DateTimeOffset?> GetLastUpdatedProgramAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult<DateTimeOffset?>(null);

    public ValueTask SetLastUpdatedProgramAsync(DateTimeOffset dateTime, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    public ValueTask<List<ScheduleJob>> GetScheduleJobsAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(new List<ScheduleJob>());

    public ValueTask<bool> DisableScheduleJobAsync(Ulid jobId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);
}
