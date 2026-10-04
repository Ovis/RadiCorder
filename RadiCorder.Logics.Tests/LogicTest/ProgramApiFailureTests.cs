using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.ApiClients;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ProgramApiFailureTests
{
    private static RadikoApiClient CreateRadiko(HttpStatusCode status, string body, ILogger<RadikoApiClient>? logger = null)
    {
        var handler = new FakeHttpMessageHandler();
        handler.AddHandler(_ => true, _ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        return new RadikoApiClient(logger ?? NullLogger<RadikoApiClient>.Instance, Mock.Of<IAppConfigurationService>(), new FakeHttpClientFactory(new HttpClient(handler)));
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

    [TestCase("ABC", 2)]
    [TestCase("CRT", 1)]
    [TestCase("RKK", 1)]
    public async Task Radiko_実APIの不完全な番組を補正またはスキップし週間番組表を取得する(string station, int expectedCount)
    {
        var xml = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", $"radiko-weekly-{station}-imperfect.xml"));
        var logger = new Mock<ILogger<RadikoApiClient>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var programs = await CreateRadiko(HttpStatusCode.OK, xml, logger.Object).GetWeeklyProgramsAsync(station);
        Assert.That(programs.Count, Is.EqualTo(expectedCount));
        Assert.That(programs.All(p => p.EndTime > p.StartTime), Is.True);
        if (station == "ABC")
        {
            var program = programs.Single(p => p.StartTime == DateTimeOffset.Parse("2026-10-06T03:00:00+09:00"));
            Assert.That(program.EndTime, Is.EqualTo(DateTimeOffset.Parse("2026-10-06T04:20:00+09:00")));
            Assert.That(program.ProgramId, Is.EqualTo("ABC_2026100603000020261006030000"));
        }
        if (station == "CRT") Assert.That(programs.Single().Title, Is.Empty);
        Assert.That(logger.Invocations.Count(i => i.Method.Name == "Log" && Equals(i.Arguments[0], LogLevel.Warning)), Is.EqualTo(1));
        Assert.That(logger.Invocations.Any(i => i.Method.Name == "Log" && Equals(i.Arguments[0], LogLevel.Error)), Is.False);
    }
}
