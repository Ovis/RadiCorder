using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RadiCorder.Logics.DependencyInjection;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Domain.Reserve;
using RadiCorder.Logics.Interfaces;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Models.NhkRadiru.JsonEntity;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.Tests.Mocks;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ProgramUpdateIsolationTests
{
    [TestCase(null)]
    [TestCase("古い予約の削除")]
    [TestCase("キーワード予約")]
    public async Task 個別障害時もNHKを更新し全体の成功日時を変更しない(string? additionalFailure)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var now = DateTimeOffset.UtcNow;
        var config = new Mock<IAppConfigurationService>();
        config.Setup(x => x.GetRadiruStationDefinitionLastCheckedAtAsync()).ReturnsAsync(now);
        var handler = new FakeHttpMessageHandler();
        handler.AddHandler(_ => true, _ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var radiru = new FakeRadiruApiClient
        {
            AreaServices = [("130", "r1")],
            Programs = [new RadiruProgramJsonEntity { Id = "new-program", Name = "新番組", StartDate = now, EndDate = now.AddHours(1) }]
        };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRadiCorderLogics();
        services.AddDbContext<RadioDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IAppConfigurationService>(config.Object);
        services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(new HttpClient(handler)));
        services.AddSingleton<IRadiruApiClient>(radiru);
        services.AddSingleton<IRadikoApiClient>(new FakeRadikoApiClient());
        services.AddSingleton(Mock.Of<IRecordingStateEventPublisher>());
        services.AddSingleton(Mock.Of<IRadikoProxyTicketService>());
        services.AddSingleton(Mock.Of<ILocalApplicationUrlService>());
        services.AddSingleton(Mock.Of<IFfmpegService>());
        if (additionalFailure != null)
        {
            var reserves = new Mock<IReserveRepository>();
            reserves.Setup(x => x.GetScheduleJobsOlderThanAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ScheduleJob>());
            reserves.Setup(x => x.GetKeywordReservesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<KeywordReserve>());
            reserves.Setup(x => x.GetKeywordReserveRadioStationsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<KeywordReserveRadioStation>());
            if (additionalFailure == "古い予約の削除")
                reserves.Setup(x => x.GetScheduleJobsOlderThanAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("cleanup failure"));
            else
                reserves.Setup(x => x.GetKeywordReservesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("keyword failure"));
            services.AddSingleton(reserves.Object);
        }
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        await db.Database.EnsureCreatedAsync();
        var lastSucceeded = now.AddDays(-2).UtcDateTime;
        db.AppConfigurations.Add(new AppConfiguration { ConfigurationName = AppConfigurationNames.LastUpdatedProgram, Val4 = lastSucceeded });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ProgramUpdateRunner>().ExecuteAsync("test");
        Assert.That(await db.NhkRadiruPrograms.AsNoTracking().CountAsync(), Is.EqualTo(1));
        var status = provider.GetRequiredService<IProgramUpdateStatusService>().GetCurrent();
        Assert.That(status.LastSucceeded, Is.False);
        Assert.That(status.Message, Does.Contain("radiko"));
        if (additionalFailure != null) Assert.That(status.Message, Does.Contain(additionalFailure));
        db.ChangeTracker.Clear();
        Assert.That((await db.AppConfigurations.SingleAsync()).Val4, Is.EqualTo(lastSucceeded));
    }
}
