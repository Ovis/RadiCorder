using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Models;

namespace RadiCorder.Features.Recording;

/// <summary>
/// 録音済みデータの更新系エンドポイントを提供する。
/// </summary>
public static class MutateRecordings
{
    /// <summary>
    /// 録音更新系エンドポイントをマッピングする。
    /// </summary>
    public static RouteGroupBuilder MapRecordingMutationEndpoints(this RouteGroupBuilder group)
    {
        group.MapDelete("/{id}", HandleDeleteAsync)
            .WithName("ApiDeleteRecording")
            .WithSummary("録音済み番組を削除する");
        group.MapPost("/bulk-delete", HandleBulkDeleteAsync)
            .WithName("ApiBulkDeleteRecordings")
            .WithSummary("録音済み番組を一括削除する");
        group.MapPost("/{id}/tags", HandleAddTagsAsync)
            .WithName("ApiAddTagsToRecording")
            .WithSummary("録音済み番組にタグを付与する");
        group.MapDelete("/{id}/tags/{tagId:guid}", HandleRemoveTagAsync)
            .WithName("ApiRemoveTagFromRecording")
            .WithSummary("録音済み番組からタグを解除する");
        group.MapPost("/tags/bulk-add", HandleBulkAddTagsAsync)
            .WithName("ApiBulkAddTagsToRecordings")
            .WithSummary("録音済み番組へタグを一括付与する");
        group.MapPost("/tags/bulk-remove", HandleBulkRemoveTagsAsync)
            .WithName("ApiBulkRemoveTagsFromRecordings")
            .WithSummary("録音済み番組からタグを一括解除する");
        group.MapPost("/listened/bulk", HandleBulkUpdateListenedAsync)
            .WithName("ApiBulkUpdateListenedState")
            .WithSummary("録音済み番組を一括で視聴済み/未視聴へ更新する");
        return group;
    }

    /// <summary>
    /// 録音済み番組を1件削除する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<bool>>, BadRequest<ApiResponse<EmptyData?>>>> HandleDeleteAsync(
        string id,
        [FromQuery] bool deleteFiles,
        [FromServices] RecordedRadioLobLogic recordedRadioLobLogic)
    {
        if (!TryParseUlid(id, out var recordUlid))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("Invalid record id."));
        }

        var exists = await recordedRadioLobLogic.CheckProgramExistsAsync(recordUlid);
        if (!exists.IsSuccess)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("recordId is required."));
        }

        if (!exists.IsExists)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("指定された番組はすでに削除されています。"));
        }

        var result = await recordedRadioLobLogic.DeleteRecordedProgramAsync(recordUlid, deleteFiles);
        return TypedResults.Ok(ApiResponse.Ok(result, result ? "削除しました。" : "削除に失敗しました。"));
    }

    /// <summary>
    /// 録音済み番組を一括削除する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<RecordingBulkDeleteResponse>>, BadRequest<ApiResponse<EmptyData?>>>> HandleBulkDeleteAsync(
        [FromBody] RecordingBulkDeleteRequest request,
        [FromServices] RecordedRadioLobLogic recordedRadioLobLogic)
    {
        if (request.RecordingIds == null || request.RecordingIds.Count == 0)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("削除対象が選択されていません。"));
        }

        // 入力IDを ULID に正規化し、重複を除去する。
        var recordingIds = request.RecordingIds
            .Select(id => Ulid.TryParse(id, out var value) ? value : (Ulid?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (recordingIds.Count == 0)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("有効な録音IDが含まれていません。"));
        }

        var successCount = 0;
        var skipCount = 0;
        var failCount = 0;
        var failedRecordingIds = new List<string>();
        var skippedRecordingIds = new List<string>();

        foreach (var id in recordingIds)
        {
            var exists = await recordedRadioLobLogic.CheckProgramExistsAsync(id);
            if (!exists.IsSuccess)
            {
                failCount++;
                failedRecordingIds.Add(id.ToString());
                continue;
            }

            if (!exists.IsExists)
            {
                skipCount++;
                skippedRecordingIds.Add(id.ToString());
                continue;
            }

            var deleted = await recordedRadioLobLogic.DeleteRecordedProgramAsync(id, request.DeleteFiles);
            if (deleted)
            {
                successCount++;
            }
            else
            {
                failCount++;
                failedRecordingIds.Add(id.ToString());
            }
        }

        var data = new RecordingBulkDeleteResponse(
            successCount,
            skipCount,
            failCount,
            failedRecordingIds,
            skippedRecordingIds);

        if (successCount == 0)
        {
            if (skipCount > 0 && failCount == 0)
            {
                return TypedResults.Ok(ApiResponse.Ok(data, "削除対象はすでに削除されています。"));
            }

            return TypedResults.BadRequest(ApiResponse.Fail("削除に失敗しました。"));
        }

        var message = (skipCount, failCount) switch
        {
            (0, 0) => $"{successCount}件を削除しました。",
            _ => $"{successCount}件削除、{skipCount}件スキップ、{failCount}件失敗しました。"
        };

        return TypedResults.Ok(ApiResponse.Ok(data, message));
    }

    /// <summary>
    /// 録音済み番組へタグを付与する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleAddTagsAsync(
        string id,
        [FromBody] RecordingTagsRequest request,
        [FromServices] TagLobLogic tagLobLogic)
    {
        if (!TryParseUlid(id, out var recordingId))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("Invalid record id."));
        }

        try
        {
            await tagLobLogic.AddTagsToRecordingAsync(recordingId, request.TagIds);
            return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
        }
        catch (DomainException ex)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(ex.UserMessage));
        }
    }

    /// <summary>
    /// 録音済み番組からタグを解除する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleRemoveTagAsync(
        string id,
        Guid tagId,
        [FromServices] TagLobLogic tagLobLogic)
    {
        if (!TryParseUlid(id, out var recordingId))
        {
            return TypedResults.BadRequest(ApiResponse.Fail("Invalid record id."));
        }

        await tagLobLogic.RemoveTagFromRecordingAsync(recordingId, tagId);
        return TypedResults.Ok(ApiResponse.Ok("更新しました。"));
    }

    /// <summary>
    /// 録音済み番組へタグを一括付与する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<TagBulkOperationResult>>, BadRequest<ApiResponse<EmptyData?>>>> HandleBulkAddTagsAsync(
        [FromBody] RecordingBulkTagRequest request,
        [FromServices] TagLobLogic tagLobLogic)
    {
        // ID変換失敗は除外して実行し、結果で失敗件数を返す。
        var recordingIds = request.RecordingIds
            .Select(id => Ulid.TryParse(id, out var value) ? value : (Ulid?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        try
        {
            var result = await tagLobLogic.BulkAddTagsToRecordingsAsync(recordingIds, request.TagIds);
            return TypedResults.Ok(ApiResponse.Ok(result));
        }
        catch (DomainException ex)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(ex.UserMessage));
        }
    }

    /// <summary>
    /// 録音済み番組からタグを一括解除する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<TagBulkOperationResult>>, BadRequest<ApiResponse<EmptyData?>>>> HandleBulkRemoveTagsAsync(
        [FromBody] RecordingBulkTagRequest request,
        [FromServices] TagLobLogic tagLobLogic)
    {
        var recordingIds = request.RecordingIds
            .Select(id => Ulid.TryParse(id, out var value) ? value : (Ulid?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToList();

        try
        {
            var result = await tagLobLogic.BulkRemoveTagsFromRecordingsAsync(recordingIds, request.TagIds);
            return TypedResults.Ok(ApiResponse.Ok(result));
        }
        catch (DomainException ex)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(ex.UserMessage));
        }
    }

    /// <summary>
    /// 録音済み番組の視聴済み状態を一括更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<RecordingBulkListenedResponse>>, BadRequest<ApiResponse<EmptyData?>>>> HandleBulkUpdateListenedAsync(
        [FromBody] RecordingBulkListenedRequest request,
        [FromServices] RecordedRadioLobLogic recordedRadioLobLogic)
    {
        if (request.RecordingIds == null || request.RecordingIds.Count == 0)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("更新対象が選択されていません。"));
        }

        var recordingIds = request.RecordingIds
            .Select(id => Ulid.TryParse(id, out var value) ? value : (Ulid?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();

        if (recordingIds.Count == 0)
        {
            return TypedResults.BadRequest(ApiResponse.Fail("有効な録音IDが含まれていません。"));
        }

        var result = await recordedRadioLobLogic.BulkUpdateListenedStateAsync(recordingIds, request.IsListened);
        var data = new RecordingBulkListenedResponse(
            result.SuccessCount,
            result.SkipCount,
            result.FailCount,
            result.FailedRecordingIds,
            result.SkippedRecordingIds);

        var message = request.IsListened
            ? $"{result.SuccessCount}件を視聴済みに更新しました。"
            : $"{result.SuccessCount}件を未視聴に更新しました。";

        if (result.SkipCount > 0 || result.FailCount > 0)
        {
            message = $"{message} スキップ:{result.SkipCount}件 / 失敗:{result.FailCount}件";
        }

        return TypedResults.Ok(ApiResponse.Ok(data, message));
    }

    /// <summary>
    /// 録音ID文字列を ULID へ変換する。
    /// </summary>
    private static bool TryParseUlid(string id, out Ulid value)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            value = default;
            return false;
        }

        return Ulid.TryParse(id, out value);
    }

    /// <summary>
    /// 一括削除結果レスポンス。
    /// </summary>
    private sealed record RecordingBulkDeleteResponse(
        int SuccessCount,
        int SkipCount,
        int FailCount,
        List<string> FailedRecordingIds,
        List<string> SkippedRecordingIds);

    /// <summary>
    /// 一括視聴状態更新結果レスポンス。
    /// </summary>
    private sealed record RecordingBulkListenedResponse(
        int SuccessCount,
        int SkipCount,
        int FailCount,
        List<string> FailedRecordingIds,
        List<string> SkippedRecordingIds);
}



