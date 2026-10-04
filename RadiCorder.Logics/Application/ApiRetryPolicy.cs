using Microsoft.Extensions.Logging;
using ZLogger;

namespace RadiCorder.Logics.Application;

/// <summary>
/// 外部API呼び出しのリトライを統一的に実行するポリシー
/// </summary>
internal static class ApiRetryPolicy
{
    /// <summary>
    /// 外部API呼び出しをリトライ付きで実行する
    /// </summary>
    /// <typeparam name="T">戻り値の型</typeparam>
    /// <param name="logger">ログ出力</param>
    /// <param name="operationName">操作名</param>
    /// <param name="action">実行処理</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <param name="maxAttempts">最大試行回数</param>
    /// <returns>実行結果</returns>
    public static async Task<T> ExecuteAsync<T>(
        ILogger logger,
        string operationName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        int maxAttempts = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        var delay = TimeSpan.FromMilliseconds(200);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action(cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                logger.ZLogWarning($"{operationName} がタイムアウトしました。再試行します。");
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                if (ex is TransientHttpRequestException { RetryAfter: { } requestedDelay })
                {
                    delay = requestedDelay > TimeSpan.Zero ? requestedDelay : TimeSpan.Zero;
                }
                logger.ZLogWarning(ex, $"{operationName} で通信エラーが発生しました。再試行します。");
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
        }

        throw new InvalidOperationException("再試行処理が終了状態を返しませんでした。");
    }
}
