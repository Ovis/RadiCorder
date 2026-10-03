using System.Net;

namespace RadiCorder.Web.Tests;

public class MvcRouteTests
{
    [TestCase("/", "RadiCorder")]
    [TestCase("/Program/Search", "searchInformationElm")]
    [TestCase("/Recorded", "RadiCorder")]
    public async Task MapEndpoints_MVCの画面を同じルートで表示する(string route, string marker)
    {
        await using var host = new WebTestHost();
        await host.StartAsync();
        using var response = await host.Client.GetAsync(route);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("text/html"));
        });
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(marker));
    }
}
