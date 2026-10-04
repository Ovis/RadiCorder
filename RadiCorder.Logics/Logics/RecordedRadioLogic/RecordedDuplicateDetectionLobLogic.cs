using Microsoft.Extensions.Logging;
using RadiCorder.Logics.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Models;
using ZLogger;

namespace RadiCorder.Logics.Logics.RecordedRadioLogic;

/// <summary>
/// 類似録音抽出ジョブの起動・実行状態管理・通知を担当する
/// </summary>
public class RecordedDuplicateDetectionLobLogic(
    ILogger<RecordedDuplicateDetectionLobLogic> logger,
    IServiceScopeFactory serviceScopeFactory,
    RecordedProgramDuplicateDetectionService detectionService,
    NotificationLobLogic notificationLobLogic,
    IRecordedDuplicateDetectionStatusPublisher? statusPublisher = null,
    IAppToastEventPublisher? appToastEventPublisher = null,
    DuplicateDetectionQueue? duplicateDetectionQueue = null)
{
    private readonly DuplicateDetectionQueue? _queue = duplicateDetectionQueue ?? ResolveQueue(serviceScopeFactory);

    private static DuplicateDetectionQueue? ResolveQueue(IServiceScopeFactory factory)
    {
        using var scope = factory.CreateScope();
        return scope.ServiceProvider.GetService<DuplicateDetectionQueue>();
    }

    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);
    private static readonly object StatusLock = new();
    private static readonly object CandidateLock = new();
    private static RecordedDuplicateDetectionStatusEntry _status = new();
    private static List<RecordedDuplicateCandidateEntry> _lastCandidates = [];

    public ValueTask<(bool IsSuccess, string Message, Exception? Error)> StartImmediateAsync(
        int lookbackDays = 30,
        int maxPhase1Groups = 100,
        string phase2Mode = "light",
        int broadcastClusterWindowHours = 48)
    {
        var normalizedLookbackDays = NormalizeLookbackDays(lookbackDays);
        var normalizedMaxPhase1Groups = NormalizeMaxPhase1Groups(maxPhase1Groups);
        var normalizedPhase2Mode = NormalizePhase2Mode(phase2Mode);
        var normalizedBroadcastClusterWindowHours = NormalizeBroadcastClusterWindowHours(broadcastClusterWindowHours);

        var accepted = !GetStatus().IsRunning && _queue?.TryEnqueue(new(
            normalizedLookbackDays, normalizedMaxPhase1Groups, normalizedPhase2Mode, normalizedBroadcastClusterWindowHours)) == true;
        return ValueTask.FromResult<(bool IsSuccess, string Message, Exception? Error)>(accepted
            ? (true, "同一番組候補チェックジョブを開始しました。完了後にお知らせへ通知されます。", null)
            : (false, "同一番組候補チェックは実行中、またはWorkerを利用できません。", null));
    }

    public RecordedDuplicateDetectionStatusEntry GetStatus()
    {
        lock (StatusLock)
        {
            return new RecordedDuplicateDetectionStatusEntry
            {
                IsRunning = _status.IsRunning,
                LastStartedAtUtc = _status.LastStartedAtUtc,
                LastCompletedAtUtc = _status.LastCompletedAtUtc,
                LastSucceeded = _status.LastSucceeded,
                LastMessage = _status.LastMessage
            };
        }
    }

    public List<RecordedDuplicateCandidateEntry> GetLastCandidates()
    {
        lock (CandidateLock)
        {
            return _lastCandidates
                .Select(x => new RecordedDuplicateCandidateEntry
                {
                    Left = new RecordedDuplicateSideEntry
                    {
                        RecordingId = x.Left.RecordingId,
                        Title = x.Left.Title,
                        StationId = x.Left.StationId,
                        StationName = x.Left.StationName,
                        StartDateTime = x.Left.StartDateTime,
                        EndDateTime = x.Left.EndDateTime,
                        DurationSeconds = x.Left.DurationSeconds
                    },
                    Right = new RecordedDuplicateSideEntry
                    {
                        RecordingId = x.Right.RecordingId,
                        Title = x.Right.Title,
                        StationId = x.Right.StationId,
                        StationName = x.Right.StationName,
                        StartDateTime = x.Right.StartDateTime,
                        EndDateTime = x.Right.EndDateTime,
                        DurationSeconds = x.Right.DurationSeconds
                    },
                    Phase1Score = x.Phase1Score,
                    AudioScore = x.AudioScore,
                    FinalScore = x.FinalScore,
                    StartTimeDiffHours = x.StartTimeDiffHours,
                    DurationDiffSeconds = x.DurationDiffSeconds
                })
                .ToList();
        }
    }

    public async ValueTask ExecuteAsync(
        string triggerSource,
        int lookbackDays,
        int maxPhase1Groups,
        string phase2Mode,
        int broadcastClusterWindowHours,
        CancellationToken cancellationToken = default)
    {
        if (!await ExecutionGate.WaitAsync(0, cancellationToken))
        {
            logger.ZLogInformation($"類似録音抽出ジョブは既に実行中のためスキップしました。 source={triggerSource}");
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        await UpdateStatusAsync(running: true, startedAt, null, false, "抽出処理を開始しました。", cancellationToken);
        lock (CandidateLock)
        {
            _lastCandidates = [];
        }

        try
        {
            logger.ZLogInformation($"類似録音抽出ジョブ開始 source={triggerSource}");
            var normalizedLookbackDays = triggerSource == "scheduled" ? 30 : NormalizeLookbackDays(lookbackDays);
            var normalizedMaxPhase1Groups = triggerSource == "scheduled" ? 100 : NormalizeMaxPhase1Groups(maxPhase1Groups);
            var normalizedPhase2Mode = triggerSource == "scheduled" ? "light" : NormalizePhase2Mode(phase2Mode);
            var normalizedBroadcastClusterWindowHours = triggerSource == "scheduled"
                ? 48
                : NormalizeBroadcastClusterWindowHours(broadcastClusterWindowHours);

            var (isSuccess, list, errorMessage, error) = await detectionService.DetectAsync(
                normalizedLookbackDays,
                normalizedMaxPhase1Groups,
                normalizedPhase2Mode,
                normalizedBroadcastClusterWindowHours,
                finalThreshold: 0.72,
                cancellationToken);

            var completedAt = DateTimeOffset.UtcNow;
            if (!isSuccess)
            {
                var message = errorMessage ?? "同一番組候補チェックに失敗しました。";
                logger.ZLogError(error, $"類似録音抽出ジョブ失敗 source={triggerSource} message={message}");
                await UpdateStatusAsync(false, startedAt, completedAt, false, message, cancellationToken);

                await notificationLobLogic.SetNotificationAsync(
                    LogLevel.Error,
                    NoticeCategory.DuplicateProgramDetection,
                    $"同一番組候補チェックに失敗しました。({triggerSource}) {message}");
                await PublishGlobalToastSafeAsync("同一番組候補のチェックに失敗しました。", false, cancellationToken);
                return;
            }

            const string summary = "同一番組候補のチェックが完了しました。";
            logger.ZLogInformation($"{summary}");

            await UpdateStatusAsync(false, startedAt, completedAt, true, summary, cancellationToken);
            lock (CandidateLock)
            {
                _lastCandidates = list;
            }

            await notificationLobLogic.SetNotificationAsync(
                LogLevel.Information,
                NoticeCategory.DuplicateProgramDetection,
                summary);
            await PublishGlobalToastSafeAsync(summary, true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var completedAt = DateTimeOffset.UtcNow;
            const string message = "類似録音抽出ジョブがキャンセルされました。";
            logger.ZLogWarning($"{message}");
            await UpdateStatusAsync(false, startedAt, completedAt, false, message, cancellationToken);
        }
        catch (Exception ex)
        {
            var completedAt = DateTimeOffset.UtcNow;
            const string message = "類似録音抽出ジョブ実行中に例外が発生しました。";
            logger.ZLogError(ex, $"{message}");
            await UpdateStatusAsync(false, startedAt, completedAt, false, message, cancellationToken);

            await notificationLobLogic.SetNotificationAsync(
                LogLevel.Error,
                NoticeCategory.DuplicateProgramDetection,
                $"{message} ({triggerSource})");
            await PublishGlobalToastSafeAsync("同一番組候補のチェックに失敗しました。", false, cancellationToken);
        }
        finally
        {
            ExecutionGate.Release();
        }
    }

    /// <summary>
    /// 同一番組候補チェック状態を更新し、SignalRへ配信する。
    /// </summary>
    private async ValueTask UpdateStatusAsync(
        bool running,
        DateTimeOffset? startedAtUtc,
        DateTimeOffset? completedAtUtc,
        bool succeeded,
        string message,
        CancellationToken cancellationToken)
    {
        RecordedDuplicateDetectionStatusEntry snapshot;
        lock (StatusLock)
        {
            _status.IsRunning = running;
            _status.LastStartedAtUtc = startedAtUtc;
            _status.LastCompletedAtUtc = completedAtUtc;
            _status.LastSucceeded = succeeded;
            _status.LastMessage = message;
            snapshot = new RecordedDuplicateDetectionStatusEntry
            {
                IsRunning = _status.IsRunning,
                LastStartedAtUtc = _status.LastStartedAtUtc,
                LastCompletedAtUtc = _status.LastCompletedAtUtc,
                LastSucceeded = _status.LastSucceeded,
                LastMessage = _status.LastMessage
            };
        }

        if (statusPublisher is null)
        {
            return;
        }

        try
        {
            await statusPublisher.PublishAsync(snapshot, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"同一番組候補チェック状態イベント通知に失敗しました。");
        }
    }

    /// <summary>
    /// 全画面トーストイベント通知を安全に実行する。
    /// </summary>
    private async ValueTask PublishGlobalToastSafeAsync(string message, bool isSuccess, CancellationToken cancellationToken)
    {
        if (appToastEventPublisher is null)
        {
            return;
        }

        try
        {
            await appToastEventPublisher.PublishAsync(
                new AppToastEvent(message, isSuccess, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"全画面トーストイベント通知に失敗しました。");
        }
    }

    private static int NormalizeLookbackDays(int lookbackDays)
    {
        return lookbackDays switch
        {
            30 or 60 or 90 or 180 or 365 => lookbackDays,
            <= 0 => 0,
            _ => 30
        };
    }

    private static int NormalizeMaxPhase1Groups(int maxPhase1Groups)
    {
        return maxPhase1Groups switch
        {
            50 or 100 or 200 or 500 => maxPhase1Groups,
            _ => 100
        };
    }

    private static string NormalizePhase2Mode(string? phase2Mode)
    {
        return string.Equals(phase2Mode, "strict", StringComparison.OrdinalIgnoreCase)
            ? "strict"
            : "light";
    }

    private static int NormalizeBroadcastClusterWindowHours(int broadcastClusterWindowHours)
    {
        return broadcastClusterWindowHours switch
        {
            24 or 36 or 48 or 72 or 96 or 168 => broadcastClusterWindowHours,
            _ => 48
        };
    }
}
