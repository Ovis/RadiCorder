using Microsoft.AspNetCore.Http.HttpResults;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Domain.AppEvent;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.NotificationLogic;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Models.ExternalImport;
using ZLogger;

namespace RadiCorder.Features.Setting;

/// <summary>
/// 外部取り込み関連の Api エンドポイントを提供する。
/// </summary>
public static class ExternalImportEndpoints
{
    private const long CsvMaxFileSize = 5 * 1024 * 1024;

    /// <summary>
    /// 外部取り込み関連エンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapExternalImportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/settings/external-import").WithTags("ExternalImportApi");
        group.MapPost("/scan", HandleScanAsync)
            .WithName("ApiExternalImportScan")
            .WithSummary("録音保存先をスキャンして未登録候補を取得する");
        group.MapPost("/export-csv", HandleExportCsv)
            .WithName("ApiExternalImportExportCsv")
            .WithSummary("候補一覧をCSVでダウンロードする");
        group.MapPost("/import-csv", HandleImportCsvAsync)
            .WithName("ApiExternalImportImportCsv")
            .WithSummary("CSVをアップロードして候補一覧を再構築する");
        group.MapPost("/save", HandleSaveAsync)
            .WithName("ApiExternalImportSave")
            .WithSummary("候補を録音済み番組へ保存する");
        group.MapPost("/maintenance/scan-missing", HandleScanMissingRecordsAsync)
            .WithName("ApiExternalImportScanMissing")
            .WithSummary("録音ファイル欠損レコードをスキャンする");
        group.MapPost("/maintenance/relink-missing", HandleRelinkMissingRecordsAsync)
            .WithName("ApiExternalImportRelinkMissing")
            .WithSummary("欠損レコードのファイル再紐付けを実行する");
        group.MapPost("/maintenance/delete-missing", HandleDeleteMissingRecordsAsync)
            .WithName("ApiExternalImportDeleteMissing")
            .WithSummary("欠損レコードをDBから削除する");
        return endpoints;
    }

    /// <summary>
    /// 録音保存先をスキャンして未登録候補を取得する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<List<ExternalImportCandidateEntry>>>, BadRequest<ApiResponse<EmptyData?>>>> HandleScanAsync(
        ILogger<ExternalImportEndpointsMarker> logger,
        ExternalRecordingImportLobLogic importLobLogic,
        IAppOperationEventPublisher appOperationEventPublisher,
        ExternalImportScanRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var applyDefaultTag = request?.ApplyDefaultTag ?? true;
            var candidates = await importLobLogic.ScanCandidatesAsync(applyDefaultTag, cancellationToken);
            await PublishOperationSafeAsync(appOperationEventPublisher, "external-import", "scan", true, "外部取り込み候補のスキャンが完了しました。", cancellationToken);
            return TypedResults.Ok(ApiResponse.Ok(candidates, "スキャンが完了しました。"));
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"外部取込スキャンでエラーが発生しました。");
            await PublishOperationSafeAsync(appOperationEventPublisher, "external-import", "scan", false, "外部取り込み候補のスキャンに失敗しました。", cancellationToken);
            return TypedResults.BadRequest(ApiResponse.Fail("スキャンに失敗しました。"));
        }
    }

    /// <summary>
    /// 候補一覧をCSVでダウンロードする。
    /// </summary>
    private static FileContentHttpResult HandleExportCsv(
        ExternalRecordingImportLobLogic importLobLogic,
        ExternalImportCandidatesRequest request)
    {
        var bytes = importLobLogic.ExportCandidatesCsv(request.Candidates);
        var fileName = $"external-import-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return TypedResults.File(bytes, "text/csv; charset=utf-8", fileName);
    }

    /// <summary>
    /// CSVをアップロードして候補一覧を再構築する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<List<ExternalImportCandidateEntry>>>, BadRequest<ApiResponse<EmptyData?>>>> HandleImportCsvAsync(
        ExternalRecordingImportLobLogic importLobLogic,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("CSVファイルが指定されていません。"));
        }

        if (file.Length > CsvMaxFileSize)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("CSVファイルサイズが上限を超えています。"));
        }

        await using var stream = file.OpenReadStream();
        var (isSuccess, candidates, errors) = await importLobLogic.ImportCandidatesCsvAsync(stream, cancellationToken);
        if (!isSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(string.Join("\n", errors)));
        }

        return TypedResults.Ok(ApiResponse.Ok(candidates, "CSVを反映しました。"));
    }

    /// <summary>
    /// 候補を録音済み番組へ保存する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<ExternalImportSaveResult>>, BadRequest<ApiResponse<ExternalImportSaveResult>>>> HandleSaveAsync(
        ExternalRecordingImportLobLogic importLobLogic,
        NotificationLobLogic notificationLobLogic,
        IAppToastEventPublisher appToastEventPublisher,
        IAppOperationEventPublisher appOperationEventPublisher,
        ExternalImportCandidatesRequest request,
        CancellationToken cancellationToken)
    {
        var result = await importLobLogic.SaveCandidatesAsync(request.Candidates, request.MarkAsListened, cancellationToken);
        if (result.Errors.Count > 0)
        {
            foreach (var error in result.Errors)
            {
                await notificationLobLogic.SetNotificationAsync(
                    LogLevel.Error,
                    NoticeCategory.RecordingError,
                    string.IsNullOrWhiteSpace(error.FilePath)
                        ? error.Message
                        : $"外部取込エラー: {Path.GetFileName(error.FilePath)} - {error.Message}");
            }

            var response = new ApiResponse<ExternalImportSaveResult>(
                false,
                result,
                new ApiError("validation_error", "入力内容にエラーがあります。"),
                "入力内容にエラーがあります。");
            await PublishOperationSafeAsync(appOperationEventPublisher, "external-import", "save", false, "外部取り込みの保存に失敗しました。", cancellationToken);
            return TypedResults.BadRequest(response);
        }

        await notificationLobLogic.SetNotificationAsync(
            LogLevel.Information,
            NoticeCategory.RecordingSuccess,
            $"外部音声ファイルを {result.SavedCount} 件取り込みました。");
        await PublishGlobalToastSafeAsync(appToastEventPublisher, $"外部取り込みが完了しました。（{result.SavedCount}件）", true, cancellationToken);
        await PublishOperationSafeAsync(appOperationEventPublisher, "external-import", "save", true, $"外部取り込みが完了しました。（{result.SavedCount}件）", cancellationToken);

        return TypedResults.Ok(ApiResponse.Ok(result, "取り込みが完了しました。"));
    }

    /// <summary>
    /// 録音ファイル欠損レコードをスキャンする。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<RecordingFileMaintenanceScanResult>>, BadRequest<ApiResponse<EmptyData?>>>> HandleScanMissingRecordsAsync(
        ILogger<ExternalImportEndpointsMarker> logger,
        RecordingFileMaintenanceLobLogic recordingFileMaintenanceLobLogic,
        IAppToastEventPublisher appToastEventPublisher,
        IAppOperationEventPublisher appOperationEventPublisher,
        bool? silent,
        CancellationToken cancellationToken)
    {
        var isSilent = silent ?? false;

        try
        {
            var result = await recordingFileMaintenanceLobLogic.ScanMissingRecordsAsync(cancellationToken);
            if (!isSilent)
            {
                await PublishGlobalToastSafeAsync(appToastEventPublisher, "メンテナンスの欠損レコード抽出が完了しました。", true, cancellationToken);
            }
            await PublishOperationSafeAsync(appOperationEventPublisher, "maintenance", "scan-missing", true, "メンテナンスの欠損レコード抽出が完了しました。", cancellationToken);
            return TypedResults.Ok(ApiResponse.Ok(result, "欠損レコードのスキャンが完了しました。"));
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"欠損レコードスキャンでエラーが発生しました。");
            await PublishOperationSafeAsync(appOperationEventPublisher, "maintenance", "scan-missing", false, "メンテナンスの欠損レコード抽出に失敗しました。", cancellationToken);
            return TypedResults.BadRequest(ApiResponse.Fail("欠損レコードのスキャンに失敗しました。"));
        }
    }

    /// <summary>
    /// 欠損レコードのファイル再紐付けを実行する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<RecordingFileMaintenanceActionResult>>, BadRequest<ApiResponse<EmptyData?>>>> HandleRelinkMissingRecordsAsync(
        ILogger<ExternalImportEndpointsMarker> logger,
        RecordingFileMaintenanceLobLogic recordingFileMaintenanceLobLogic,
        IAppToastEventPublisher appToastEventPublisher,
        IAppOperationEventPublisher appOperationEventPublisher,
        RecordingMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var ids = request.RecordingIds
                .Select(id => Ulid.TryParse(id, out var value) ? value : (Ulid?)null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            var result = await recordingFileMaintenanceLobLogic.RelinkMissingRecordsAsync(ids, cancellationToken);
            var message = BuildMaintenanceActionSummary("再紐付け処理が完了しました。", result);
            await PublishGlobalToastSafeAsync(appToastEventPublisher, message, true, cancellationToken);
            await PublishOperationSafeAsync(appOperationEventPublisher, "maintenance", "relink-missing", true, message, cancellationToken);
            return TypedResults.Ok(ApiResponse.Ok(result, "再紐付け処理が完了しました。"));
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"欠損レコード再紐付けでエラーが発生しました。");
            await PublishOperationSafeAsync(appOperationEventPublisher, "maintenance", "relink-missing", false, "メンテナンスの再紐付けに失敗しました。", cancellationToken);
            return TypedResults.BadRequest(ApiResponse.Fail("再紐付け処理に失敗しました。"));
        }
    }

    /// <summary>
    /// 欠損レコードをDBから削除する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<RecordingFileMaintenanceActionResult>>, BadRequest<ApiResponse<EmptyData?>>>> HandleDeleteMissingRecordsAsync(
        ILogger<ExternalImportEndpointsMarker> logger,
        RecordingFileMaintenanceLobLogic recordingFileMaintenanceLobLogic,
        IAppToastEventPublisher appToastEventPublisher,
        IAppOperationEventPublisher appOperationEventPublisher,
        RecordingMaintenanceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var ids = request.RecordingIds
                .Select(id => Ulid.TryParse(id, out var value) ? value : (Ulid?)null)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToList();

            var result = await recordingFileMaintenanceLobLogic.DeleteMissingRecordsAsync(ids, cancellationToken);
            var message = BuildMaintenanceActionSummary("欠損レコード削除が完了しました。", result);
            await PublishGlobalToastSafeAsync(appToastEventPublisher, message, true, cancellationToken);
            await PublishOperationSafeAsync(appOperationEventPublisher, "maintenance", "delete-missing", true, message, cancellationToken);
            return TypedResults.Ok(ApiResponse.Ok(result, "欠損レコード削除が完了しました。"));
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"欠損レコード削除でエラーが発生しました。");
            await PublishOperationSafeAsync(appOperationEventPublisher, "maintenance", "delete-missing", false, "メンテナンスの欠損レコード削除に失敗しました。", cancellationToken);
            return TypedResults.BadRequest(ApiResponse.Fail("欠損レコード削除に失敗しました。"));
        }
    }

    /// <summary>
    /// 全画面トーストイベント通知を安全に実行する。
    /// </summary>
    private static async ValueTask PublishGlobalToastSafeAsync(
        IAppToastEventPublisher appToastEventPublisher,
        string message,
        bool isSuccess,
        CancellationToken cancellationToken)
    {
        try
        {
            await appToastEventPublisher.PublishAsync(
                new AppToastEvent(message, isSuccess, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch
        {
            // トースト配信失敗は本処理の結果に影響させない
        }
    }

    /// <summary>
    /// 機能別イベント通知を安全に実行する。
    /// </summary>
    private static async ValueTask PublishOperationSafeAsync(
        IAppOperationEventPublisher appOperationEventPublisher,
        string category,
        string action,
        bool succeeded,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await appOperationEventPublisher.PublishAsync(
                new AppOperationEvent(category, action, succeeded, message, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch
        {
            // イベント配信失敗は本処理の結果に影響させない
        }
    }

    private static string BuildMaintenanceActionSummary(
        string title,
        RecordingFileMaintenanceActionResult result)
    {
        return $"{title} 成功:{result.SuccessCount} / スキップ:{result.SkipCount} / 失敗:{result.FailCount}";
    }

    /// <summary>
    /// 外部取込候補保存リクエスト。
    /// </summary>
    public sealed class ExternalImportCandidatesRequest
    {
        public List<ExternalImportCandidateEntry> Candidates { get; set; } = [];
        public bool MarkAsListened { get; set; }
    }

    /// <summary>
    /// 外部取込スキャン要求。
    /// </summary>
    public sealed class ExternalImportScanRequest
    {
        public bool ApplyDefaultTag { get; set; } = true;
    }

    /// <summary>
    /// メンテナンス対象録音ID要求。
    /// </summary>
    public sealed class RecordingMaintenanceRequest
    {
        public List<string> RecordingIds { get; set; } = [];
    }

    /// <summary>
    /// ExternalImportEndpoints 用のロガーカテゴリ型。
    /// </summary>
    private sealed class ExternalImportEndpointsMarker;
}



