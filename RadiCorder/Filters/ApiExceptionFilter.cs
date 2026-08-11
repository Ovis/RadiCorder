using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RadiCorder.Logics.Errors;
using ZLogger;

namespace RadiCorder.Filters;

/// <summary>
/// API向けの例外レスポンスを統一するフィルター
/// </summary>
public sealed class ApiExceptionFilter(
    ILogger<ApiExceptionFilter> logger,
    IHostEnvironment environment) : IExceptionFilter
{
    /// <summary>
    /// 例外発生時のレスポンスを生成する
    /// </summary>
    public void OnException(ExceptionContext context)
    {
        if (!context.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            return;
        }

        if (context.Exception is DomainException domainException)
        {
            logger.ZLogWarning(domainException, $"APIドメイン例外が発生しました。");
            context.Result = new BadRequestObjectResult(new
            {
                Message = domainException.UserMessage
            });
            context.ExceptionHandled = true;
            return;
        }

        logger.ZLogError(context.Exception, $"APIで未処理の例外が発生しました。");

        var message = environment.IsDevelopment()
            ? context.Exception.Message
            : "予期しないエラーが発生しました。";

        context.Result = new ObjectResult(new
        {
            Message = message
        })
        {
            StatusCode = StatusCodes.Status500InternalServerError
        };
        context.ExceptionHandled = true;
    }
}
