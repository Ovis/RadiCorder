using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.Services;

namespace RadiCorder.Web.Tests;

public class RadikoStreamingTests
{
    [TestCase("/api/programs/radiko-proxy")]
    [TestCase("/api/programs/radiko-proxy/master.m3u8")]
    public async Task Proxy_既存のルートとticketとplaylist応答を維持する(string route)
    {
        await using var host = new WebTestHost();
        await host.StartAsync(services => services.AddHttpClient(HttpClientNames.Radiko)
            .ConfigurePrimaryHttpMessageHandler(() => new FixtureHandler()));
        var ticket = host.App.Services.GetRequiredService<IRadikoProxyTicketService>().IssueTokenTicket("fixture-token");
        using var response = await host.Client.GetAsync($"{route}?target={Uri.EscapeDataString("https://radiko.jp/media.m3u8")}&proxyKey={ticket}");
        var content = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/vnd.apple.mpegurl"));
            Assert.That(content, Does.Contain($"proxyKey={ticket}"));
            Assert.That(content, Does.Contain(Uri.EscapeDataString("https://radiko.jp/segment.aac")));
            Assert.That(content, Does.Not.Contain("fixture-token"));
        });
    }

    [TestCase("https://example.com/media.m3u8", "valid", "Invalid proxy target.")]
    [TestCase("https://radiko.jp/media.m3u8", "expired", "Invalid proxy credential.")]
    public async Task Proxy_入力エラーのHTTP状態と本文を維持する(string target, string credential, string message)
    {
        await using var host = new WebTestHost();
        await host.StartAsync();
        var ticket = credential == "valid"
            ? host.App.Services.GetRequiredService<IRadikoProxyTicketService>().IssueTokenTicket("fixture-token")
            : "expired";
        using var response = await host.Client.GetAsync($"/api/programs/radiko-proxy?target={Uri.EscapeDataString(target)}&proxyKey={ticket}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(System.Text.Json.JsonSerializer.Deserialize<string>(await response.Content.ReadAsStringAsync()), Is.EqualTo(message));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.That(request.Headers.GetValues("X-Radiko-Authtoken").Single(), Is.EqualTo("fixture-token"));
            Assert.That(request.Headers.GetValues("User-Agent").Single(), Is.EqualTo("RadiCorder-fixture"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("#EXTM3U\n#EXTINF:5,\nsegment.aac\n", Encoding.UTF8, "application/vnd.apple.mpegurl")
            });
        }
    }
}
