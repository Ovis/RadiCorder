using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Providers.Radiko;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ProviderParserTests
{
    [TestCase("new RadikoJSPlayer('a','b','key123',{")]
    [TestCase("new RadikoJSPlayer(player, options, 'key123', {")]
    [TestCase("new RadikoJSPlayer(\n\"a\",\n\"b\", \"key123\", {")]
    public void JSの改行と引用符に対応する(string javascript)
    {
        Assert.That(RadikoAuthenticationParser.TryGetPartialKey(javascript, out var key), Is.True);
        Assert.That(key, Is.EqualTo("key123"));
    }

    [TestCase("invalid")]
    [TestCase("new RadikoJSPlayer('a','b','',{")]
    public void キー欠落を成功扱いにしない(string javascript)
    {
        Assert.That(RadikoAuthenticationParser.TryGetPartialKey(javascript, out _), Is.False);
    }

    [Test]
    public void XML解析をHTTPとDBなしで確認できる()
    {
        var xml = XElement.Parse("<prog ft='20261004120000' to='20261004130000'><title>番組</title><ts_in_ng>0</ts_in_ng></prog>");
        var parsed = RadikoProgramParser.ParseProgram(xml, "TBS");
        Assert.That(parsed.Program, Is.Not.Null);
        Assert.That(parsed.Program!.ProgramId, Is.EqualTo("TBS_2026100412000020261004130000"));
        Assert.That(parsed.UsedFallback, Is.False);
    }

    [Test]
    public async Task RetryAfter待機中もキャンセルを伝播して余分な要求を送らない()
    {
        using var handler = new RateLimitedHandler();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.CatchAsync<OperationCanceledException>(async () => await HttpClientExecutionHelper.SendWithRetryAsync(
            NullLogger.Instance, client, "rate-limit", () => new HttpRequestMessage(HttpMethod.Get, "https://example.test"), cancellationToken: cancellation.Token));
        Assert.That(handler.RequestCount, Is.EqualTo(1));
        await Task.CompletedTask;
    }

    private sealed class RateLimitedHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(10));
            return Task.FromResult(response);
        }
    }
}
