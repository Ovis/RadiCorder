using System.Net;
using Microsoft.Extensions.Logging;

namespace RadiCorder.Logics.Application;

/// <summary>
/// HttpClient実行処理の共通ヘルパー
/// </summary>
public static class HttpClientExecutionHelper
{
    /// <summary>
    /// リトライ付きでHTTPリクエストを実行する
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="httpClient">HTTPクライアント</param>
    /// <param name="operationName">操作名</param>
    /// <param name="requestFactory">リクエスト生成処理</param>
    /// <param name="userAgent"></param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>HTTPレスポンス</returns>
    public static async ValueTask<HttpResponseMessage> SendWithRetryAsync(
        ILogger logger,
        HttpClient httpClient,
        string operationName,
        Func<HttpRequestMessage> requestFactory,
        string? userAgent = null,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, ValueTask>? beforeAttempt = null)
    {
        return await ApiRetryPolicy.ExecuteAsync(
            logger,
            operationName,
            async ct =>
            {
                if (beforeAttempt != null) await beforeAttempt(ct);
                using var request = requestFactory();
                if (!string.IsNullOrWhiteSpace(userAgent) &&
                    !request.Headers.Contains("User-Agent"))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                }
                var response = await httpClient.SendAsync(request, ct);
                if (IsTransientFailure(response.StatusCode))
                {
                    var retryAfter = response.Headers.RetryAfter;
                    var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow);
                    // 長時間の待機を要求された場合は、その場で再試行せず呼び出し側へ返す。
                    if (delay > TimeSpan.FromHours(1)) return response;
                    var statusCode = response.StatusCode;
                    response.Dispose();
                    throw new TransientHttpRequestException($"{operationName} request failed: {(int)statusCode}", statusCode, delay);
                }

                return response;
            },
            cancellationToken);
    }

    private static bool IsTransientFailure(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status == (int)HttpStatusCode.RequestTimeout ||
               status == (int)HttpStatusCode.TooManyRequests ||
               status >= 500;
    }
}

internal sealed class TransientHttpRequestException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter)
    : HttpRequestException(message, null, statusCode)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
