using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Models.Enums;

namespace RadiCorder.Logics.Domain.Reserve;

/// <summary>
/// 予約関連の永続化を担うリポジトリ
/// </summary>
public interface IReserveRepository
{
    /// <summary>
    /// 録音予約一覧を取得する
    /// </summary>
    ValueTask<List<ScheduleJob>> GetScheduleJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音予約を追加する
    /// </summary>
    ValueTask AddScheduleJobAsync(ScheduleJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音予約を取得する
    /// </summary>
    ValueTask<ScheduleJob?> GetScheduleJobByIdAsync(Ulid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 状態変更の判定には、追跡済みの古い値ではなく現在の保存値を使う。
    /// </summary>
    ValueTask<ScheduleJob?> GetScheduleJobSnapshotAsync(Ulid id, CancellationToken cancellationToken = default)
        => GetScheduleJobByIdAsync(id, cancellationToken);

    /// <summary>
    /// 番組IDで録音予約を取得する
    /// </summary>
    ValueTask<ScheduleJob?> GetScheduleJobByProgramIdAsync(string programId, CancellationToken cancellationToken = default);

    /// <summary>
    /// サービスと番組IDで予約を取得する。旧実装は既存の一覧取得で互換性を保つ。
    /// </summary>
    async ValueTask<ScheduleJob?> GetScheduleJobByProgramKeyAsync(ProgramKey key, CancellationToken cancellationToken = default)
        => (await GetScheduleJobsAsync(cancellationToken)).FirstOrDefault(x => x.ServiceKind == key.ServiceKind && x.ProgramId == key.ProgramId);

    /// <summary>
    /// 録音予約を削除する
    /// </summary>
    ValueTask RemoveScheduleJobAsync(ScheduleJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音予約を更新する
    /// </summary>
    ValueTask UpdateScheduleJobAsync(ScheduleJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// 実行時刻・状態を上書きせず、現在有効な予約だけを無効化する。
    /// </summary>
    async ValueTask<bool> TryDisableScheduleJobAsync(Ulid id, CancellationToken cancellationToken = default)
    {
        var job = await GetScheduleJobByIdAsync(id, cancellationToken);
        if (job == null || !job.IsEnabled) return false;
        job.IsEnabled = false;
        await UpdateScheduleJobAsync(job, cancellationToken);
        return true;
    }

    /// <summary>
    /// 読取後に状態が変わっていない無効な予約だけを、明示的に再有効化する。
    /// </summary>
    async ValueTask<bool> TryReactivateScheduleJobAsync(ScheduleJob job, ScheduleJobState expectedState, CancellationToken cancellationToken = default)
    {
        var current = await GetScheduleJobByIdAsync(job.Id, cancellationToken);
        if (current == null || current.IsEnabled || current.State != expectedState) return false;
        await UpdateScheduleJobAsync(job, cancellationToken);
        return true;
    }

    /// <summary>
    /// マージン更新対象の予約を取得する
    /// </summary>
    ValueTask<List<ScheduleJob>> GetScheduleJobsNeedingDurationUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定日時より古い予約を取得する
    /// </summary>
    ValueTask<List<ScheduleJob>> GetScheduleJobsOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約一覧を取得する
    /// </summary>
    ValueTask<List<KeywordReserve>> GetKeywordReservesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 次に採番するキーワード予約の並び順を取得する
    /// </summary>
    ValueTask<int> GetNextKeywordReserveSortOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約の放送局一覧を取得する
    /// </summary>
    ValueTask<List<KeywordReserveRadioStation>> GetKeywordReserveRadioStationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約を取得する
    /// </summary>
    ValueTask<KeywordReserve?> GetKeywordReserveByIdAsync(Ulid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約を追加する
    /// </summary>
    ValueTask AddKeywordReserveAsync(KeywordReserve reserve, IEnumerable<KeywordReserveRadioStation> stations, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約を更新する
    /// </summary>
    ValueTask UpdateKeywordReserveAsync(KeywordReserve reserve, IEnumerable<KeywordReserveRadioStation> stations, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約の並び順を更新する
    /// </summary>
    ValueTask ReorderKeywordReservesAsync(IReadOnlyList<Ulid> orderedIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約を削除する
    /// </summary>
    ValueTask<bool> DeleteKeywordReserveAsync(Ulid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約の放送局設定を削除する
    /// </summary>
    ValueTask DeleteKeywordReserveRadioStationsAsync(Ulid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// キーワード予約に紐づく録音予約を取得する
    /// </summary>
    ValueTask<List<ScheduleJob>> GetScheduleJobsByKeywordReserveIdAsync(Ulid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 番組IDで録音予約の存在を確認する
    /// </summary>
    ValueTask<bool> ExistsScheduleJobByProgramIdAsync(string programId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音予約とキーワード予約の関連を一括追加する（重複は無視）
    /// </summary>
    ValueTask AddScheduleJobKeywordReserveRelationsAsync(
        IEnumerable<ScheduleJobKeywordReserveRelation> relations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定したキーワード予約との関連を録音予約から解除する
    /// </summary>
    ValueTask RemoveKeywordReserveFromScheduleJobsAsync(
        Ulid keywordReserveId,
        IEnumerable<Ulid> scheduleJobIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音予約ごとの関連キーワード予約ID一覧を取得する
    /// </summary>
    ValueTask<Dictionary<Ulid, List<Ulid>>> GetKeywordReserveIdsByScheduleJobIdsAsync(
        IEnumerable<Ulid> scheduleJobIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音予約を一括削除する
    /// </summary>
    ValueTask RemoveScheduleJobsAsync(IEnumerable<ScheduleJob> jobs, CancellationToken cancellationToken = default);

    /// <summary>
    /// 録音予約を一括追加する
    /// </summary>
    ValueTask AddScheduleJobsAsync(IEnumerable<ScheduleJob> jobs, CancellationToken cancellationToken = default);
}
