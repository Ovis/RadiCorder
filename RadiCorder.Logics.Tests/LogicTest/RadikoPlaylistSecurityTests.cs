using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Services.Streaming;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RadikoPlaylistSecurityTests
{
    [TestCase("http://127.0.0.1/private", false)]
    [TestCase("https://other.example/private", false)]
    [TestCase("https://radiko.jp:8443/private", false)]
    [TestCase("https://user@radiko.jp/private", false)]
    [TestCase("https://radiko.jp/media.m3u8", true)]
    public async Task マスターの接続先を検証してから認証ヘッダーを送る(string mediaUrl, bool allowed)
    {
        var requests = new List<Uri>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(requests.Count == 1
                ? $"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1\n{mediaUrl}\n" : "#EXTM3U\n#EXTINF:5,\nsegment.aac\n") };
        }));
        var subject = new RadikoPlaylistClient(new FakeHttpClientFactory(client), Mock.Of<IAppConfigurationService>());
        var action = async () => await subject.ResolveLiveAsync(NullLogger.Instance, new Uri("https://radiko.jp/master.m3u8"), "token", null, default);
        if (allowed) Assert.That((await action()).StatusCode, Is.EqualTo(200));
        else Assert.That(action, Throws.InstanceOf<HttpRequestException>());
        Assert.That(requests.Count, Is.EqualTo(allowed ? 2 : 1));
    }

    [TestCase("https://other.example/private", false)]
    [TestCase("/media.m3u8", true)]
    public async Task リダイレクト先も検証する(string location, bool allowed)
    {
        var requests = new List<Uri>();
        using var client = new HttpClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!);
            if (requests.Count == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
                return response;
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var subject = new RadikoPlaylistClient(new FakeHttpClientFactory(client), Mock.Of<IAppConfigurationService>());
        var action = async () => { using var response = await subject.SendAsync(new Uri("https://radiko.jp/master.m3u8"), "token", default); };
        if (allowed) await action();
        else Assert.That(action, Throws.InstanceOf<HttpRequestException>());
        Assert.That(requests.Count, Is.EqualTo(allowed ? 2 : 1));
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request, cancellationToken));
    }
}
