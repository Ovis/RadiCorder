using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RadiCorder.Endpoints;

/// <summary>
/// Api 系の共通エンドポイントを登録する。
/// </summary>
public static class ApiEndpoints
{
    /// <summary>
    /// Api 系エンドポイントをマッピングする。
    /// </summary>
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api").WithTags("Api");
        group.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        return endpoints;
    }
}

