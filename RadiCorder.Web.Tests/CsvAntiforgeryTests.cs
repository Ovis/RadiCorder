using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace RadiCorder.Web.Tests;

public class CsvAntiforgeryTests
{
    [TestCase(true, 400, "CSVファイルが指定されていません。")]
    [TestCase(false, 400, null)]
    public async Task CSVフォームで検証ミドルウェアが働きエンドポイントへ到達する(bool validToken, int status, string? expectedMessage)
    {
        await using var host = new WebTestHost();
        await host.StartAsync();
        host.App.MapGet("/fixture-antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            antiforgery.GetAndStoreTokens(context).RequestToken);
        using var tokenResponse = await host.Client.GetAsync("/fixture-antiforgery");
        var token = await tokenResponse.Content.ReadAsStringAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/settings/external-import/import-csv");
        if (validToken)
        {
            request.Headers.Add("RequestVerificationToken", token);
            request.Headers.Add("Cookie", string.Join("; ", tokenResponse.Headers.GetValues("Set-Cookie").Select(x => x.Split(';')[0])));
        }
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(""), "file", "empty.csv");
        request.Content = form;
        using var response = await host.Client.SendAsync(request);
        Assert.That((int)response.StatusCode, Is.EqualTo(status));
        if (expectedMessage != null) Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(expectedMessage));
    }
}
