using System.Net.Http;

namespace RadiCorder.Logics.Tests.Mocks;

/// <summary>
/// テスト用 IHttpClientFactory
/// </summary>
public sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    /// <summary>
    /// 名前付き HttpClient を返す
    /// </summary>
    public HttpClient CreateClient(string name)
    {
        return client;
    }
}

