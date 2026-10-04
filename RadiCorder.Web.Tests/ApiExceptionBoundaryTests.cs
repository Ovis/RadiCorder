using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using RadiCorder.Filters;
using RadiCorder.Logics.Errors;

namespace RadiCorder.Web.Tests;

public class ApiExceptionBoundaryTests
{
    [Test]
    public async Task 実際のAPIに適用して本番の未処理例外をHTMLや内部情報として返さない()
    {
        await using var host = new WebTestHost();
        await host.StartAsync(environmentName: "Production");
        using var response = await host.Client.PostAsJsonAsync("/api/settings/external-import/export-csv", new { candidates = (object?)null });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.RootElement.GetProperty("message").GetString(), Is.EqualTo("予期しないエラーが発生しました。"));
    }

    [Test]
    public async Task ドメイン例外を400のJSONで返す()
    {
        await using var host = new WebTestHost();
        await host.StartAsync(configureApp: app => app.MapGroup("/api/fixture").AddEndpointFilter<ApiExceptionEndpointFilter>()
            .MapGet("/domain-error", (Func<string>)ThrowDomainException));
        using var response = await host.Client.GetAsync("/api/fixture/domain-error");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.RootElement.GetProperty("message").GetString(), Is.EqualTo("fixture-error"));
    }

    private static string ThrowDomainException() => throw new DomainException("fixture-error");
}
