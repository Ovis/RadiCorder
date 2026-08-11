using System.Net;
using System.Net.Http;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// HttpClient のレスポンスを差し替えるテスト用ハンドラー
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Predicate<HttpRequestMessage> Match, Func<HttpRequestMessage, HttpResponseMessage> Factory)> _handlers = [];

    /// <summary>
    /// 条件とレスポンス生成処理を追加する
    /// </summary>
    public void AddHandler(Predicate<HttpRequestMessage> match, Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        _handlers.Add((match, factory));
    }

    /// <summary>
    /// 条件と固定レスポンスを追加する
    /// </summary>
    public void AddHandler(Predicate<HttpRequestMessage> match, HttpResponseMessage response)
    {
        AddHandler(match, _ => response);
    }

    /// <summary>
    /// 受信したリクエストに対する応答を返す
    /// </summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        foreach (var (match, factory) in _handlers)
        {
            if (match(request))
            {
                return Task.FromResult(factory(request));
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

