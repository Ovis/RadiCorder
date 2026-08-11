using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Domain.Recording;
using ZLogger;

namespace RadiCorder.Logics.UseCases.Recording;

/// <summary>
/// 録音フローを統括するユースケース
/// </summary>
public class RecordingOrchestrator(
    ILogger<RecordingOrchestrator> logger,
    IEnumerable<IRecordingSource> sources,
    IMediaStorageService storage,
    IMediaTranscodeService transcoder,
    IRecordingRepository repository,
    IRecordingStateEventPublisher recordingStateEventPublisher,
    IAppToastEventPublisher? appToastEventPublisher = null)
{
    /// <summary>
    /// 録音処理を実行する
    /// </summary>
    /// <param name="command">録音コマンド</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>録音結果</returns>
    public async ValueTask<RecordingResult> RecordAsync(RecordingCommand command, CancellationToken cancellationToken = default)
    {
        Ulid? recordingId = null;
        MediaPath? mediaPath = null;
        var isCommitted = false;
        var shouldPreserveTempFile = false;

        // 例外が起きても録音フローを中断しないように安全に状態更新を行う
        async ValueTask UpdateStateSafeAsync(RecordingState state, string? message)
        {
            if (recordingId == null)
            {
                return;
            }

            try
            {
                await repository.UpdateStateAsync(recordingId.Value, state, message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"録音状態の更新に失敗しました。");
                return;
            }

            try
            {
                await recordingStateEventPublisher.PublishAsync(
                    new RecordingStateChangedEvent(
                        recordingId.Value,
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
        async ValueTask PublishGlobalToastSafeAsync(string message, bool isSuccess)
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

        async ValueTask PublishFailureToastSafeAsync(string reason)
        {
            var title = string.IsNullOrWhiteSpace(command.ProgramName) ? "録音" : $"{command.ProgramName} の録音";
            await PublishGlobalToastSafeAsync($"{title}に失敗しました。理由: {reason}", false);
        }

        // コミット前に失敗した場合は一時ファイルを確実に削除する
        async ValueTask CleanupTempSafeAsync()
        {
            if (mediaPath == null || isCommitted || shouldPreserveTempFile)
            {
                return;
            }

            try
            {
                await storage.CleanupTempAsync(mediaPath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.ZLogError(ex, $"一時ファイルの削除に失敗しました。");
            }
        }

        var source = sources.FirstOrDefault(s => s.CanHandle(command.ServiceKind));
        if (source == null)
        {
            const string errorMessage = "未対応のサービスです。";
            await PublishFailureToastSafeAsync(errorMessage);
            return new RecordingResult(false, null, errorMessage);
        }

        try
        {
            var maxAttempts = command.IsTimeFree ? 2 : 1;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                if (attempt > 1 && source is IRecordingSourceRetryHandler retryHandler)
                {
                    logger.ZLogWarning($"録音ソースの再試行準備を実行します。 programId={command.ProgramId} attempt={attempt}/{maxAttempts}");
                    await retryHandler.PrepareForRetryAsync(command, cancellationToken);
                }

                var sourceResult = await source.PrepareAsync(command, cancellationToken);
                if (mediaPath == null)
                {
                    mediaPath = await storage.PrepareAsync(sourceResult.ProgramInfo, sourceResult.Options, cancellationToken);
                }

                if (recordingId == null)
                {
                    recordingId = await repository.CreateAsync(sourceResult.ProgramInfo, mediaPath, sourceResult.Options, cancellationToken);
                    await UpdateStateSafeAsync(RecordingState.Recording, null);
                }

                var ok = await transcoder.RecordAsync(sourceResult, mediaPath, cancellationToken);
                if (ok)
                {
                    try
                    {
                        mediaPath = await storage.CommitAsync(mediaPath, cancellationToken);
                        isCommitted = true;
                        await repository.UpdateFilePathAsync(recordingId.Value, mediaPath, cancellationToken);
                        await UpdateStateSafeAsync(RecordingState.Completed, null);
                        await PublishGlobalToastSafeAsync($"{command.ProgramName} の録音が完了しました。", true);

                        return new RecordingResult(true, recordingId, null);
                    }
                    catch (Exception commitEx)
                    {
                        shouldPreserveTempFile = true;
                        SaveFailedFallbackResult? fallbackResult = null;

                        try
                        {
                            fallbackResult = await storage.SaveFailedAsync(
                                mediaPath,
                                new SaveFailedFallbackMetadata(
                                    RecordedAt: DateTimeOffset.UtcNow,
                                    ProgramId: sourceResult.ProgramInfo.ProgramId,
                                    StationId: sourceResult.ProgramInfo.StationId,
                                    Title: sourceResult.ProgramInfo.Title,
                                    OriginalDestinationPath: mediaPath.FinalFilePath,
                                    ErrorType: commitEx.GetType().Name,
                                    ErrorMessage: commitEx.Message,
                                    ExpectedTags: new Dictionary<string, string>
                                    {
                                        ["title"] = sourceResult.ProgramInfo.Title,
                                        ["artist"] = sourceResult.ProgramInfo.Performer,
                                        ["comment"] = sourceResult.ProgramInfo.Description,
                                        ["date"] = sourceResult.ProgramInfo.StartTime.ToString("O")
                                    }),
                                cancellationToken);
                            logger.ZLogError(
                                commitEx,
                                $"保存先への移動に失敗したため退避保存しました。 originalPath={mediaPath.FinalFilePath} fallbackPath={fallbackResult.FilePath} metadataPath={fallbackResult.MetadataPath}");
                        }
                        catch (Exception fallbackEx)
                        {
                            logger.ZLogError(
                                fallbackEx,
                                $"保存先への移動失敗後の退避保存にも失敗しました。 tempPath={mediaPath.TempFilePath} originalPath={mediaPath.FinalFilePath}");
                        }

                        var commitErrorMessage = fallbackResult is null
                            ? "録音は完了しましたが、保存先エラーのため正式保存できませんでした。"
                            : "録音は完了しましたが、保存先エラーのため正式保存できませんでした（退避済み）";

                        await UpdateStateSafeAsync(RecordingState.Failed, commitErrorMessage);
                        await PublishGlobalToastSafeAsync(commitErrorMessage, false);
                        return new RecordingResult(false, recordingId, commitErrorMessage);
                    }
                }

                if (command.IsTimeFree && attempt < maxAttempts)
                {
                    logger.ZLogWarning($"タイムフリー録音に失敗したため、認証情報を更新して再試行します。 programId={command.ProgramId} attempt={attempt}/{maxAttempts}");
                    await CleanupTempSafeAsync();
                    continue;
                }

                var errorMessage = command.IsTimeFree
                    ? "タイムフリー録音チャンク取得に失敗しました。"
                    : command.IsOnDemand
                        ? "聞き逃し配信録音に失敗しました。"
                        : "録音処理に失敗しました。";
                await UpdateStateSafeAsync(RecordingState.Failed, errorMessage);
                await PublishFailureToastSafeAsync(errorMessage);
                return new RecordingResult(false, recordingId, errorMessage);
            }

            const string retryErrorMessage = "録音処理に失敗しました。";
            await UpdateStateSafeAsync(RecordingState.Failed, retryErrorMessage);
            await PublishFailureToastSafeAsync(retryErrorMessage);
            return new RecordingResult(false, recordingId, retryErrorMessage);
        }
        catch (OperationCanceledException)
        {
            const string errorMessage = "録音処理がキャンセルされました。";
            logger.ZLogWarning($"{errorMessage}");
            await UpdateStateSafeAsync(RecordingState.Failed, errorMessage);
            await PublishFailureToastSafeAsync(errorMessage);
            return new RecordingResult(false, recordingId, errorMessage);
        }
        catch (DomainException ex)
        {
            logger.ZLogWarning(ex, $"録音処理でドメイン例外が発生しました。");
            await UpdateStateSafeAsync(RecordingState.Failed, ex.UserMessage);
            await PublishFailureToastSafeAsync(ex.UserMessage);
            return new RecordingResult(false, recordingId, ex.UserMessage);
        }
        catch (Exception ex)
        {
            const string errorMessage = "録音処理で例外が発生しました。";
            logger.ZLogError(ex, $"{errorMessage}");
            await UpdateStateSafeAsync(RecordingState.Failed, errorMessage);
            await PublishFailureToastSafeAsync(errorMessage);
            return new RecordingResult(false, recordingId, errorMessage);
        }
        finally
        {
            await CleanupTempSafeAsync();
        }
    }
}
