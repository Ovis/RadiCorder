using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.Domain.Recording;
using ZLogger;

namespace RadiCorder.Logics.UseCases.Recording;

/// <summary>
/// 1回の録音に固有の状態、通知、一時ファイルの後処理を管理する。
/// </summary>
internal sealed class RecordingExecutionContext(
    RecordingCommand command,
    ILogger<RecordingOrchestrator> logger,
    IMediaStorageService storage,
    IRecordingRepository repository,
    IRecordingStateEventPublisher recordingStateEventPublisher,
    IAppToastEventPublisher? appToastEventPublisher,
    CancellationToken cancellationToken)
{
    public Ulid? RecordingId;
    public MediaPath? MediaPath;
    public bool IsCommitted;
    public bool ShouldPreserveTempFile;

    // 例外が起きても録音フローを中断しないように安全に状態更新を行う
    public async ValueTask UpdateStateSafeAsync(RecordingState state, string? message)
    {
        if (RecordingId == null)
        {
            return;
        }

        try
        {
            await UpdateStateRequiredAsync(state, message);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"録音状態の更新に失敗しました。");
            return;
        }

    }

    public async ValueTask UpdateStateRequiredAsync(RecordingState state, string? message)
    {
        if (RecordingId == null) return;
        await repository.UpdateStateAsync(RecordingId.Value, state, message, cancellationToken);
        try
        {
            await recordingStateEventPublisher.PublishAsync(
                new RecordingStateChangedEvent(
                    RecordingId.Value,
                    state,
                    message,
                    DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, $"録音状態変更イベントの通知に失敗しました。");
        }
    }

    /// <summary>
    /// 全画面トースト通知を安全に実行する
    /// </summary>
    public async ValueTask PublishGlobalToastSafeAsync(string message, bool isSuccess)
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

    public async ValueTask PublishFailureToastSafeAsync(string reason)
    {
        var title = string.IsNullOrWhiteSpace(command.ProgramName) ? "録音" : $"{command.ProgramName} の録音";
        await PublishGlobalToastSafeAsync($"{title}に失敗しました。理由: {reason}", false);
    }

    // コミット前に失敗した場合は一時ファイルを確実に削除する
    public async ValueTask CleanupTempSafeAsync()
    {
        if (MediaPath == null || IsCommitted || ShouldPreserveTempFile)
        {
            return;
        }

        try
        {
            await storage.CleanupTempAsync(MediaPath, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"一時ファイルの削除に失敗しました。");
        }
    }

}
