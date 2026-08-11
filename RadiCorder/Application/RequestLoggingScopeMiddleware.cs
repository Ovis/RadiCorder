namespace RadiCorder.Application;

/// <summary>
/// リクエスト単位の相関情報をログスコープへ設定するミドルウェア。
/// </summary>
public class RequestLoggingScopeMiddleware(RequestDelegate next, ILogger<RequestLoggingScopeMiddleware> logger)
{
    /// <summary>
    /// リクエスト処理を実行する。
    /// </summary>
    /// <param name="context">HTTPコンテキスト</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var scopeState = new Dictionary<string, object?>
        {
            ["TraceId"] = context.TraceIdentifier,
            ["RequestPath"] = context.Request.Path.Value ?? string.Empty,
            ["Method"] = context.Request.Method
        };

        using (logger.BeginScope(scopeState))
        {
            await next(context);
        }
    }
}

