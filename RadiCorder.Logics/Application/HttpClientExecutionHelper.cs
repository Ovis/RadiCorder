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
        CancellationToken cancellationToken = default)
    {
        return await ApiRetryPolicy.ExecuteAsync(
            logger,
            operationName,
            async ct =>
            {
                using var request = requestFactory();
                if (!string.IsNullOrWhiteSpace(userAgent) &&
                    !request.Headers.Contains("User-Agent"))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                }
                var response = await httpClient.SendAsync(request, ct);
                if (IsTransientFailure(response.StatusCode))
                {
                    response.Dispose();
                    throw new HttpRequestException($"{operationName} request failed: {(int)response.StatusCode}");
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
