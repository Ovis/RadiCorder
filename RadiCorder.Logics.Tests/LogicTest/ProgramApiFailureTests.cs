using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.ApiClients;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ProgramApiFailureTests
{
    private static RadikoApiClient CreateRadiko(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpMessageHandler();
        handler.AddHandler(_ => true, _ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        return new RadikoApiClient(NullLogger<RadikoApiClient>.Instance, Mock.Of<IAppConfigurationService>(), new FakeHttpClientFactory(new HttpClient(handler)));
    }

    [TestCase(HttpStatusCode.InternalServerError, "unavailable")]
    [TestCase(HttpStatusCode.OK, "<changed />")]
    [TestCase(HttpStatusCode.OK, "broken XML")]
    [TestCase(HttpStatusCode.OK, "<radiko><station id='TBS'/><station id='OTHER'/></radiko>")]
    [TestCase(HttpStatusCode.OK, "<radiko><station id='TBS'><prog ft='invalid' to='invalid' /></station></radiko>")]
    public void Radiko_通信や形式の異常を正常な空データにしない(HttpStatusCode status, string body)
    {
        Assert.ThrowsAsync<DomainException>(async () => await CreateRadiko(status, body).GetWeeklyProgramsAsync("TBS"));
    }

    [Test]
    public async Task Radiko_対象局の正常な空番組表は許容する()
    {
        var programs = await CreateRadiko(HttpStatusCode.OK, "<radiko><station id='TBS'><progs /></station></radiko>").GetWeeklyProgramsAsync("TBS");
        Assert.That(programs, Is.Empty);
    }

    [Test]
    public void Radiko_キャンセルは呼び出し側へ返す()
    {
        Assert.ThrowsAsync<OperationCanceledException>(async () => await CreateRadiko(HttpStatusCode.OK, "<radiko />").GetWeeklyProgramsAsync("TBS", new CancellationToken(true)));
    }
}
