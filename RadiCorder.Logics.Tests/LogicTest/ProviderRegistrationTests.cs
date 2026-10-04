using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RadiCorder.Logics.DependencyInjection;
using RadiCorder.Logics.Domain.ProgramSchedule;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.ProgramScheduleLogic;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ProviderRegistrationTests
{
    [Test]
    public async Task 第三サービスの検索を共通処理の分岐追加なしで登録できる()
    {
        var services = CreateServices();
        services.AddScoped<IProgramSearchProvider, OtherSearch>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<ProgramSearchService>();
        var result = await search.SearchAsync(new ProgramSearchRequest(new ProgramSearchEntity { OrderKindString = "ProgramStartDateTimeAsc" },
            new Dictionary<RadioServiceKind, IReadOnlyList<string>> { [RadioServiceKind.Other] = ["station"] }));
        Assert.That(result.Single().ProgramId, Is.EqualTo("other:station"));
        Assert.That(result.Single().ServiceKind, Is.EqualTo(RadioServiceKind.Other));
    }

    [Test]
    public void 同じサービスの検索アダプター重複を黙って採用しない()
    {
        var services = CreateServices();
        services.AddScoped<IProgramSearchProvider, OtherSearch>();
        services.AddScoped<IProgramSearchProvider, OtherSearch>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Throws<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<ProgramSearchService>());
    }

    [Test]
    public async Task 第三サービスの番組詳細を共通処理の分岐追加なしで登録できる()
    {
        var services = CreateServices();
        services.AddScoped<IProgramLookupProvider, OtherLookup>();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var program = await scope.ServiceProvider.GetRequiredService<ProgramScheduleLobLogic>().GetProgramAsync("other-program", RadioServiceKind.Other);
        Assert.That(program!.ProgramId, Is.EqualTo("other-program"));
        Assert.That(program.ServiceKind, Is.EqualTo(RadioServiceKind.Other));
    }

    [Test]
    public void 同じサービスの録音ソース重複を黙って採用しない()
    {
        var services = CreateServices();
        var duplicate = new Mock<IRecordingSource>();
        duplicate.Setup(x => x.CanHandle(RadioServiceKind.Radiko)).Returns(true);
        services.AddSingleton(duplicate.Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<RadiCorder.Logics.UseCases.Recording.RecordingOrchestrator>();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await orchestrator.RecordAsync(new RecordingCommand(RadioServiceKind.Radiko, "p", "番組", false, 0, 0)));
    }

    private sealed class OtherLookup : IProgramLookupProvider
    {
        public RadioServiceKind ServiceKind => RadioServiceKind.Other;
        public ValueTask<RadioProgramEntry?> GetAsync(string programId)
            => ValueTask.FromResult<RadioProgramEntry?>(new() { ProgramId = programId, ServiceKind = ServiceKind });
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRadiCorderLogics();
        services.AddSingleton(Mock.Of<IAppConfigurationService>());
        services.AddSingleton(Mock.Of<IRecordingStateEventPublisher>());
        services.AddSingleton(Mock.Of<IFfmpegService>());
        services.AddSingleton(Mock.Of<IRadikoProxyTicketService>());
        services.AddSingleton(Mock.Of<ILocalApplicationUrlService>());
        services.AddDbContext<RadioDbContext>(o => o.UseSqlite("Data Source=:memory:"));
        return services;
    }

    private sealed class OtherSearch : IProgramSearchProvider
    {
        public RadioServiceKind ServiceKind => RadioServiceKind.Other;
        public ValueTask<List<ProgramForApiEntry>> SearchAsync(ProgramSearchEntity filters, IReadOnlyList<string> stationIds)
            => ValueTask.FromResult(new List<ProgramForApiEntry> { new() { ProgramId = $"other:{stationIds.Single()}", ServiceKind = ServiceKind } });
    }
}
