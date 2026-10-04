using RadiCorder.Logics.Errors;

namespace RadiCorder.Logics.Domain.ProgramSchedule;

/// <summary>
/// 対象ごとの取得失敗を保持し、成功した対象の更新を継続する。
/// </summary>
public sealed class ProgramSyncReport
{
    public int SucceededTargets { get; private set; }
    public List<(string Target, Exception Error)> Failures { get; } = [];
    public bool IsSuccess => Failures.Count == 0;

    public async ValueTask RunAsync(string target, Func<ValueTask> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await action();
            SucceededTargets++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Failures.Add((target, ex));
        }
    }

    public void ThrowIfFailed()
    {
        if (!IsSuccess) throw new DomainException($"番組表更新の一部に失敗しました。対象: {string.Join(", ", Failures.Select(x => x.Target))}", new AggregateException(Failures.Select(x => x.Error)));
    }
}
