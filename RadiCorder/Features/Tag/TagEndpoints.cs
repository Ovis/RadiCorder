using Microsoft.AspNetCore.Http.HttpResults;
using RadiCorder.Features.Shared.Models;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.TagLogic;
using RadiCorder.Logics.Models;

namespace RadiCorder.Features.Tag;

/// <summary>
/// タグ管理の Api エンドポイントを提供する。
/// </summary>
public static class TagEndpoints
{
    /// <summary>
    /// タグ管理エンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tags").WithTags("TagsApi");
        group.MapGet("/", HandleGetTagsAsync)
            .WithName("ApiGetTags")
            .WithSummary("タグ一覧を取得する");
        group.MapPost("/", HandleCreateTagAsync)
            .WithName("ApiCreateTag")
            .WithSummary("タグを作成する");
        group.MapPatch("/{id:guid}", HandleUpdateTagAsync)
            .WithName("ApiUpdateTag")
            .WithSummary("タグを更新する");
        group.MapDelete("/{id:guid}", HandleDeleteTagAsync)
            .WithName("ApiDeleteTag")
            .WithSummary("タグを削除する");
        group.MapPost("/merge", HandleMergeTagAsync)
            .WithName("ApiMergeTag")
            .WithSummary("タグを統合する");
        return endpoints;
    }

    /// <summary>
    /// タグ一覧を取得する。
    /// </summary>
    private static async Task<Ok<ApiResponse<List<TagEntry>>>> HandleGetTagsAsync(
        TagLobLogic tagLobLogic,
        string keyword = "")
    {
        var list = await tagLobLogic.GetTagsAsync(keyword);
        return TypedResults.Ok(ApiResponse.Ok(list));
    }

    /// <summary>
    /// タグを作成する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<TagEntry>>, BadRequest<ApiResponse<EmptyData?>>>> HandleCreateTagAsync(
        TagUpsertRequest request,
        TagLobLogic tagLobLogic)
    {
        try
        {
            var created = await tagLobLogic.CreateTagAsync(request.Name);
            return TypedResults.Ok(ApiResponse.Ok(created, "作成しました。"));
        }
        catch (DomainException ex)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(ex.UserMessage));
        }
    }

    /// <summary>
    /// タグを更新する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<TagEntry>>, BadRequest<ApiResponse<EmptyData?>>>> HandleUpdateTagAsync(
        Guid id,
        TagUpsertRequest request,
        TagLobLogic tagLobLogic)
    {
        try
        {
            var updated = await tagLobLogic.UpdateTagAsync(id, request.Name);
            return TypedResults.Ok(ApiResponse.Ok(updated, "更新しました。"));
        }
        catch (DomainException ex)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(ex.UserMessage));
        }
    }

    /// <summary>
    /// タグを削除する。
    /// </summary>
    private static async Task<Ok<ApiResponse<EmptyData?>>> HandleDeleteTagAsync(
        Guid id,
        TagLobLogic tagLobLogic)
    {
        await tagLobLogic.DeleteTagAsync(id);
        return TypedResults.Ok(ApiResponse.Ok("削除しました。"));
    }

    /// <summary>
    /// タグを統合する。
    /// </summary>
    private static async Task<Results<Ok<ApiResponse<EmptyData?>>, BadRequest<ApiResponse<EmptyData?>>>> HandleMergeTagAsync(
        TagMergeRequest request,
        TagLobLogic tagLobLogic)
    {
        try
        {
            await tagLobLogic.MergeTagAsync(request.FromTagId, request.ToTagId);
            return TypedResults.Ok(ApiResponse.Ok("統合しました。"));
        }
        catch (DomainException ex)
        {
            return TypedResults.BadRequest(ApiResponse.Fail(ex.UserMessage));
        }
    }
}



