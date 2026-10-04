using RadiCorder.Logics.Errors;
using ZLogger;

namespace RadiCorder.Filters;

/// <summary>
/// Minimal APIの未処理例外をAPI向けのJSON応答へ変換する。
/// </summary>
public sealed class ApiExceptionEndpointFilter(
    ILogger<ApiExceptionEndpointFilter> logger,
    IHostEnvironment environment) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { return await next(context); }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (DomainException ex)
        {
            logger.ZLogWarning(ex, $"APIドメイン例外が発生しました。");
            return Results.BadRequest(new { Message = ex.UserMessage });
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, $"APIで未処理の例外が発生しました。");
            var message = environment.IsDevelopment() ? ex.Message : "予期しないエラーが発生しました。";
            return Results.Json(new { Message = message }, statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
