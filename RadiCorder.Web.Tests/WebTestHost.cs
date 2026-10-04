using RadiCorder.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using RadiCorder.DependencyInjection;
using RadiCorder.Features.Program;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Web.Tests;

/// <summary>
/// 外部通信とWorkerを起動せずに実際のHTTP境界を確認するhost。
/// </summary>
internal sealed class WebTestHost : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"radicorder-web-test-{Guid.NewGuid():N}");
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task StartAsync(Action<IServiceCollection>? configure = null)
    {
        Directory.CreateDirectory(_root);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ProgramEndpoints).Assembly.FullName,
            EnvironmentName = Environments.Development,
            ContentRootPath = _root
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RadiCorder:DbDirectory"] = _root
        });
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ProgramEndpoints).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddOpenApi();
        builder.Services.AddSignalR();
        builder.Services.AddDbDi(builder.Configuration, builder.Environment);
        builder.Services.AddConfigDi(builder.Configuration);
        builder.Services.AddLogicDiCollection(builder.Configuration);
        foreach (var descriptor in builder.Services.Where(x => x.ServiceType == typeof(IHostedService) &&
                     x.ImplementationType != null && typeof(BackgroundService).IsAssignableFrom(x.ImplementationType)).ToArray())
        {
            builder.Services.Remove(descriptor);
        }
        var config = new Mock<IAppConfigurationService>();
        config.SetupGet(x => x.TemporaryFileSaveDir).Returns(_root);
        config.SetupGet(x => x.RecordFileSaveDir).Returns(_root);
        config.SetupGet(x => x.ExternalServiceUserAgent).Returns("RadiCorder-fixture");
        config.SetupGet(x => x.IsRadikoAreaFree).Returns(true);
        builder.Services.AddSingleton(config.Object);
        configure?.Invoke(builder.Services);
        App = builder.Build();
        // 外部データ取得を伴うStartupTaskは実行しない。DBは本番と同じmigrationを適用する。
        using (var scope = App.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RadioDbContext>().Database.MigrateAsync();
        }
        App.UseAntiforgery();
        App.MapRadiCorderEndpoints();
        App.MapOpenApi();
        await App.StartAsync();
        Client = new HttpClient { BaseAddress = new Uri(App.Urls.Single()) };
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (App != null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }
}
