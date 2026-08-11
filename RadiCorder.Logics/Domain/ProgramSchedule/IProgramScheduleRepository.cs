using RadiCorder.Logics.Models;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Domain.ProgramSchedule;

/// <summary>
/// 番組表データの永続化を担うリポジトリ
/// </summary>
public interface IProgramScheduleRepository
{
    /// <summary>
    /// 指定時刻に放送中のradiko番組を取得する
    /// </summary>
    ValueTask<List<RadikoProgram>> GetRadikoNowOnAirAsync(DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定時刻に放送中のらじる★らじる番組を取得する
    /// </summary>
    ValueTask<List<NhkRadiruProgram>> GetRadiruNowOnAirAsync(DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default);

    /// <summary>
    /// radiko番組一覧を日付と局で取得する
    /// </summary>
    ValueTask<List<RadikoProgram>> GetRadikoProgramsAsync(DateOnly date, string stationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// radiko番組をIDで取得する
    /// </summary>
    ValueTask<RadikoProgram?> GetRadikoProgramByIdAsync(string programId, CancellationToken cancellationToken = default);

    /// <summary>
    /// radiko放送局ID一覧を取得する
    /// </summary>
    ValueTask<List<string>> GetRadikoStationIdsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// radiko番組を追加する
    /// </summary>
    ValueTask AddRadikoProgramsIfMissingAsync(IEnumerable<RadikoProgram> programs, CancellationToken cancellationToken = default);

    /// <summary>
    /// 全放送局について、指定日までのradiko番組表データが揃っているかを判定する
    /// </summary>
    ValueTask<bool> HasRadikoProgramsForAllStationsThroughAsync(DateOnly targetDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// radiko番組を検索する
    /// </summary>
    ValueTask<List<RadikoProgram>> SearchRadikoProgramsAsync(ProgramSearchEntity searchEntity, DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 古いradiko番組を削除する
    /// </summary>
    ValueTask DeleteOldRadikoProgramsAsync(DateOnly deleteDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// らじる★らじる番組一覧を日付/エリア/局で取得する
    /// </summary>
    ValueTask<List<NhkRadiruProgram>> GetRadiruProgramsAsync(DateOnly date, string areaId, string stationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// らじる★らじる番組をIDで取得する
    /// </summary>
    ValueTask<NhkRadiruProgram?> GetRadiruProgramByIdAsync(string programId, CancellationToken cancellationToken = default);

    /// <summary>
    /// らじる★らじる番組を追加または更新する
    /// </summary>
    ValueTask UpsertRadiruProgramsAsync(IEnumerable<NhkRadiruProgram> programs, CancellationToken cancellationToken = default);

    /// <summary>
    /// らじる★らじる番組を検索する
    /// </summary>
    ValueTask<List<NhkRadiruProgram>> SearchRadiruProgramsAsync(ProgramSearchEntity searchEntity, DateTimeOffset standardDateTimeOffset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 古いらじる★らじる番組を削除する
    /// </summary>
    ValueTask DeleteOldRadiruProgramsAsync(DateOnly deleteDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// 番組表の最終更新日時を取得する
    /// </summary>
    ValueTask<DateTimeOffset?> GetLastUpdatedProgramAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 番組表の最終更新日時を更新する
    /// </summary>
    ValueTask SetLastUpdatedProgramAsync(DateTimeOffset dateTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// スケジュール済みジョブ一覧を取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>ジョブ一覧</returns>
    ValueTask<List<ScheduleJob>> GetScheduleJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定したスケジュールジョブを無効化する
    /// </summary>
    /// <param name="jobId">対象ジョブID</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>対象が存在し無効化できた場合はtrue</returns>
    ValueTask<bool> DisableScheduleJobAsync(Ulid jobId, CancellationToken cancellationToken = default);
}
