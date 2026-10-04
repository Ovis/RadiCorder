using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.DependencyInjection;
using RadiCorder.Logics.Logics.RecordedRadioLogic;
using RadiCorder.Logics.Models;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class DuplicateDetectionWorkerTests
{
    [Test]
    public async Task 手動要求をWorkerで実行し停止時は処理をキャンセルして状態を確定する()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var config = Mock.Of<IAppConfigurationService>();
        using var db = new RadioDbContext(new DbContextOptionsBuilder<RadioDbContext>().UseSqlite("Data Source=:memory:").Options);
        var detector = new Mock<RecordedProgramDuplicateDetectionService>(
            NullLogger<RecordedProgramDuplicateDetectionService>.Instance, config, new ConfigurationBuilder().Build(), db);
        detector.Setup(x => x.DetectAsync(60, 200, "strict", 24, 0.72, It.IsAny<CancellationToken>()))
            .Returns(async (int _, int _, string _, int _, double _, CancellationToken token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
                return (true, new List<RecordedDuplicateCandidateEntry>(), null, null);
            });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRadiCorderLogics();
        services.AddSingleton(config);
        services.AddSingleton(detector.Object);
        services.AddSingleton(db);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var logic = scope.ServiceProvider.GetRequiredService<RecordedDuplicateDetectionLobLogic>();
        Assert.That((await logic.StartImmediateAsync(60, 200, "strict", 24)).IsSuccess, Is.True);
        Assert.That((await logic.StartImmediateAsync()).IsSuccess, Is.False, "待機中の要求を際限なく追加しない");
        using var worker = new DuplicateDetectionWorker(provider.GetRequiredService<DuplicateDetectionQueue>(),
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DuplicateDetectionWorker>.Instance);
        await worker.StartAsync(default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(deadline.Token);
        Assert.That(logic.GetStatus().IsRunning, Is.False);
        Assert.That(logic.GetStatus().LastSucceeded, Is.False);
        Assert.That(logic.GetStatus().LastMessage, Does.Contain("キャンセル"));
    }
}
