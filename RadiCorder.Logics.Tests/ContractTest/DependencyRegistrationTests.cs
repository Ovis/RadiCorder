using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RadiCorder.Logics.Application;
using RadiCorder.Logics.DependencyInjection;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.ContractTest;

/// <summary>
/// 共通DI登録のライフタイムとHTTP設定を確認する
/// </summary>
public class DependencyRegistrationTests
{
    [Test]
    public void AddRadiCorderLogics_WebやWorkerを起動せずに外部通信サービスを利用できる()
    {
        var services = new ServiceCollection();
        services.AddRadiCorderLogics();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        foreach (var name in new[] { HttpClientNames.Radiko, HttpClientNames.Radiru, HttpClientNames.Webhook, HttpClientNames.GitHub })
        {
            using var client = factory.CreateClient(name);
            Assert.That(client.Timeout, Is.EqualTo(TimeSpan.FromSeconds(15)), name);
        }
        Assert.Multiple(() =>
        {
            Assert.That(services.Any(x => x.ServiceType == typeof(IHostedService)), Is.False);
            Assert.That(services.Where(x => x.ServiceType == typeof(IRecordingSource)).Select(x => x.Lifetime),
                Is.EqualTo(new[] { ServiceLifetime.Scoped, ServiceLifetime.Scoped }));
            Assert.That(services.Single(x => x.ServiceType == typeof(IFfmpegService)).Lifetime, Is.EqualTo(ServiceLifetime.Transient));
        });
    }
}
