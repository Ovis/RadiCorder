using Microsoft.Extensions.Logging;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Models.Enums;
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
        var execution = new RecordingExecutionContext(
            command, logger, storage, repository, recordingStateEventPublisher, appToastEventPublisher, cancellationToken);

        var candidates = sources.Where(s => s.CanHandle(command.ServiceKind)).Take(2).ToList();
        if (candidates.Count > 1) throw new InvalidOperationException($"録音ソースが重複登録されています。service={command.ServiceKind}");
        var source = candidates.SingleOrDefault();
        if (source == null)
        {
            const string errorMessage = "未対応のサービスです。";
            await execution.PublishFailureToastSafeAsync(errorMessage);
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
                if (execution.MediaPath == null)
                {
                    execution.MediaPath = await storage.PrepareAsync(sourceResult.ProgramInfo, sourceResult.Options, cancellationToken);
                }

                if (execution.RecordingId == null)
                {
                    execution.RecordingId = await repository.CreateAsync(sourceResult.ProgramInfo, execution.MediaPath, sourceResult.Options, cancellationToken);
                    await execution.UpdateStateSafeAsync(RecordingState.Recording, null);
                }

                var ok = await transcoder.RecordAsync(sourceResult, execution.MediaPath, cancellationToken);
                if (ok)
                {
                    try
                    {
                        execution.MediaPath = storage is IRecoverableMediaStorageService recoverableStorage
                            ? await recoverableStorage.CommitRecordingAsync(execution.MediaPath, execution.RecordingId.Value, command.ScheduleJobId, cancellationToken)
                            : await storage.CommitAsync(execution.MediaPath, cancellationToken);
                        execution.IsCommitted = true;
                        await repository.UpdateFilePathAsync(execution.RecordingId.Value, execution.MediaPath, cancellationToken);
                        // 完了の永続化失敗を成功として返さない。通知の失敗は許容する。
                        await execution.UpdateStateRequiredAsync(RecordingState.Completed, null);
                        if (string.IsNullOrEmpty(command.ScheduleJobId) && storage is IRecoverableMediaStorageService finalizedStorage)
                        {
                            finalizedStorage.CompleteFinalization(execution.RecordingId.Value);
                        }
                        await execution.PublishGlobalToastSafeAsync($"{command.ProgramName} の録音が完了しました。", true);

                        return new RecordingResult(true, execution.RecordingId, null);
                    }
                    catch (Exception commitEx)
                    {
                        if (execution.IsCommitted)
                        {
                            const string message = "録音ファイルは保存済みですが、DBの確定に失敗しました。再起動時に復旧します。";
                            logger.ZLogError(commitEx, $"{message} recordingId={execution.RecordingId} path={execution.MediaPath.FinalFilePath}");
                            await execution.PublishGlobalToastSafeAsync(message, false);
                            return new RecordingResult(false, execution.RecordingId, message) { ErrorCode = ScheduleJobErrorCode.FinalizeFailed };
                        }
                        execution.ShouldPreserveTempFile = true;
                        SaveFailedFallbackResult? fallbackResult = null;

                        try
                        {
                            fallbackResult = await storage.SaveFailedAsync(
                                execution.MediaPath,
                                new SaveFailedFallbackMetadata(
                                    RecordedAt: DateTimeOffset.UtcNow,
                                    ProgramId: sourceResult.ProgramInfo.ProgramId,
                                    StationId: sourceResult.ProgramInfo.StationId,
                                    Title: sourceResult.ProgramInfo.Title,
                                    OriginalDestinationPath: execution.MediaPath.FinalFilePath,
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
                                $"保存先への移動に失敗したため退避保存しました。 originalPath={execution.MediaPath.FinalFilePath} fallbackPath={fallbackResult.FilePath} metadataPath={fallbackResult.MetadataPath}");
                        }
                        catch (Exception fallbackEx)
                        {
                            logger.ZLogError(
                                fallbackEx,
                                $"保存先への移動失敗後の退避保存にも失敗しました。 tempPath={execution.MediaPath.TempFilePath} originalPath={execution.MediaPath.FinalFilePath}");
                        }

                        var commitErrorMessage = fallbackResult is null
                            ? "録音は完了しましたが、保存先エラーのため正式保存できませんでした。"
                            : "録音は完了しましたが、保存先エラーのため正式保存できませんでした（退避済み）";

                        await execution.UpdateStateSafeAsync(RecordingState.Failed, commitErrorMessage);
                        await execution.PublishGlobalToastSafeAsync(commitErrorMessage, false);
                        return new RecordingResult(false, execution.RecordingId, commitErrorMessage) { ErrorCode = ScheduleJobErrorCode.FinalizeFailed };
                    }
                }

                if (command.IsTimeFree && attempt < maxAttempts)
                {
                    logger.ZLogWarning($"タイムフリー録音に失敗したため、認証情報を更新して再試行します。 programId={command.ProgramId} attempt={attempt}/{maxAttempts}");
                    await execution.CleanupTempSafeAsync();
                    continue;
                }

                var errorMessage = command.IsTimeFree
                    ? "タイムフリー録音チャンク取得に失敗しました。"
                    : command.IsOnDemand
                        ? "聞き逃し配信録音に失敗しました。"
                        : "録音処理に失敗しました。";
                await execution.UpdateStateSafeAsync(RecordingState.Failed, errorMessage);
                await execution.PublishFailureToastSafeAsync(errorMessage);
                return new RecordingResult(false, execution.RecordingId, errorMessage) { ErrorCode = ScheduleJobErrorCode.SourceUnavailable };
            }

            const string retryErrorMessage = "録音処理に失敗しました。";
            await execution.UpdateStateSafeAsync(RecordingState.Failed, retryErrorMessage);
            await execution.PublishFailureToastSafeAsync(retryErrorMessage);
            return new RecordingResult(false, execution.RecordingId, retryErrorMessage);
        }
        catch (OperationCanceledException)
        {
            const string errorMessage = "録音処理がキャンセルされました。";
            logger.ZLogWarning($"{errorMessage}");
            await execution.UpdateStateSafeAsync(RecordingState.Failed, errorMessage);
            await execution.PublishFailureToastSafeAsync(errorMessage);
            return new RecordingResult(false, execution.RecordingId, errorMessage) { ErrorCode = ScheduleJobErrorCode.Cancelled };
        }
        catch (DomainException ex)
        {
            logger.ZLogWarning(ex, $"録音処理でドメイン例外が発生しました。");
            await execution.UpdateStateSafeAsync(RecordingState.Failed, ex.UserMessage);
            await execution.PublishFailureToastSafeAsync(ex.UserMessage);
            return new RecordingResult(false, execution.RecordingId, ex.UserMessage) { ErrorCode = RecordingJobErrorClassifier.ClassifyError(ex) };
        }
        catch (Exception ex)
        {
            const string errorMessage = "録音処理で例外が発生しました。";
            logger.ZLogError(ex, $"{errorMessage}");
            await execution.UpdateStateSafeAsync(RecordingState.Failed, errorMessage);
            await execution.PublishFailureToastSafeAsync(errorMessage);
            return new RecordingResult(false, execution.RecordingId, errorMessage) { ErrorCode = RecordingJobErrorClassifier.ClassifyError(ex) };
        }
        finally
        {
            await execution.CleanupTempSafeAsync();
        }
    }
}
