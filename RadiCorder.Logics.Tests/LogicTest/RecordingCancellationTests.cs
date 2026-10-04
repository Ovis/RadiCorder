using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RadiCorder.Logics.DependencyInjection;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Errors;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RecordingCancellationTests
{
    [TestCase("任意の表示文言")]
    [TestCase("認証 ffmpeg disk 配信")]
    public void 失敗の型を表示文言より優先する(string message)
    {
        var error = new RecordingFailureException(ScheduleJobErrorCode.FinalizeFailed, message);
        Assert.That(RecordingJobErrorClassifier.ClassifyError(error), Is.EqualTo(ScheduleJobErrorCode.FinalizeFailed));
    }

    [Test]
    public async Task ホスト停止は準備中ジョブの終了状態保存まで待機する()
    {
        var root = Path.Combine(Path.GetTempPath(), $"radi-stop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var config = new Mock<IAppConfigurationService>();
            config.SetupGet(x => x.RecordFileSaveDir).Returns(root);
            config.SetupGet(x => x.TemporaryFileSaveDir).Returns(root);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRadiCorderLogics();
            services.AddSingleton<IAppConfigurationService>(config.Object);
            services.AddSingleton(Mock.Of<IRecordingStateEventPublisher>());
            services.AddSingleton(Mock.Of<IRadikoProxyTicketService>());
            services.AddSingleton(Mock.Of<ILocalApplicationUrlService>());
            services.AddSingleton(Mock.Of<IFfmpegService>());
            services.AddSingleton<IRecordingScheduleWakeup, RecordingScheduleWakeup>();
            services.AddSingleton<RecordingScheduleBackgroundService>();
            services.AddDbContext<RadioDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "test.db")}"));
            await using var provider = services.BuildServiceProvider();
            var id = Ulid.NewUlid();
            using (var scope = provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
                await db.Database.EnsureCreatedAsync();
                db.ScheduleJob.Add(new ScheduleJob { Id = id, ProgramId = "p", ServiceKind = RadioServiceKind.Radiko, State = ScheduleJobState.Pending, IsEnabled = true, RecordingType = RecordingType.RealTime, StartDateTime = DateTimeOffset.UtcNow.AddSeconds(8), EndDateTime = DateTimeOffset.UtcNow.AddHours(1) });
                await db.SaveChangesAsync();
            }
            var worker = provider.GetRequiredService<RecordingScheduleBackgroundService>();
            await worker.StartAsync(default);
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                while (true)
                {
                    using var scope = provider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
                    if (await db.ScheduleJob.AsNoTracking().Where(x => x.Id == id).Select(x => x.State).SingleAsync(deadline.Token) == ScheduleJobState.Preparing) break;
                    await Task.Delay(20, deadline.Token);
                }
            }
            finally { await worker.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(5)); }
            using var finalScope = provider.CreateScope();
            var job = await finalScope.ServiceProvider.GetRequiredService<RadioDbContext>().ScheduleJob.AsNoTracking().SingleAsync();
            Assert.That(job.State, Is.EqualTo(ScheduleJobState.Cancelled));
            Assert.That(job.IsEnabled, Is.False);
            Assert.That(job.CompletedUtc, Is.Not.Null);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task 準備待機のキャンセルでも独立したトークンで終了状態を保存する(bool cancelByJobId)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRadiCorderLogics();
        services.AddSingleton(Mock.Of<IAppConfigurationService>());
        services.AddSingleton(Mock.Of<IRecordingStateEventPublisher>());
        services.AddSingleton(Mock.Of<IRadikoProxyTicketService>());
        services.AddSingleton(Mock.Of<ILocalApplicationUrlService>());
        services.AddSingleton(Mock.Of<IFfmpegService>());
        services.AddDbContext<RadioDbContext>(o => o.UseSqlite(connection));
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        await db.Database.EnsureCreatedAsync();
        var job = new ScheduleJob { Id = Ulid.NewUlid(), ProgramId = "p", ServiceKind = RadioServiceKind.Radiko, State = ScheduleJobState.Queued, IsEnabled = true, RecordingType = RecordingType.RealTime, StartDateTime = DateTimeOffset.UtcNow.AddHours(1), EndDateTime = DateTimeOffset.UtcNow.AddHours(2) };
        db.ScheduleJob.Add(job);
        await db.SaveChangesAsync();
        using var cancellation = new CancellationTokenSource();
        var execution = scope.ServiceProvider.GetRequiredService<RecordingJobExecutor>().ExecuteAsync(job.Id, cancellation.Token).AsTask();
        // SQLiteの準備状態への更新は最初の待機前に完了する。
        Assert.That(await db.ScheduleJob.AsNoTracking().Where(x => x.Id == job.Id).Select(x => x.State).SingleAsync(), Is.EqualTo(ScheduleJobState.Preparing));
        if (cancelByJobId) RadiCorder.Logics.Logics.RecordingLogic.RecordingCancellationRegistry.Cancel(job.Id.ToString());
        else await cancellation.CancelAsync();
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        var completed = await db.ScheduleJob.AsNoTracking().SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(completed.State, Is.EqualTo(ScheduleJobState.Cancelled));
            Assert.That(completed.LastErrorCode, Is.EqualTo(ScheduleJobErrorCode.Cancelled));
            Assert.That(completed.IsEnabled, Is.False);
            Assert.That(completed.CompletedUtc, Is.Not.Null);
        });
    }
}
