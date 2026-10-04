using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RadiCorder.Logics.BackgroundServices;
using RadiCorder.Logics.DependencyInjection;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Logics.ReserveLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class ReservationReactivationTests
{
    private string _root = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"radi-reactivation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var config = new Mock<IAppConfigurationService>();
        config.SetupGet(x => x.RecordFileSaveDir).Returns(_root);
        config.SetupGet(x => x.TemporaryFileSaveDir).Returns(_root);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRadiCorderLogics();
        services.AddSingleton(config.Object);
        services.AddSingleton(Mock.Of<IRecordingStateEventPublisher>());
        services.AddSingleton(Mock.Of<IRadikoProxyTicketService>());
        services.AddSingleton(Mock.Of<ILocalApplicationUrlService>());
        services.AddSingleton(Mock.Of<IFfmpegService>());
        services.AddSingleton<IRecordingScheduleWakeup, RecordingScheduleWakeup>();
        services.AddDbContext<RadioDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(_root, "test.db")}"));
        _provider = services.BuildServiceProvider();
        using var scope = _provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RadioDbContext>().Database.EnsureCreatedAsync();
    }

    [TearDown]
    public async Task Cleanup()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task 予約削除のDB障害を成功扱いせず次の削除にも持ち越さない()
    {
        var firstId = await SeedAsync(ScheduleJobState.Pending, true);
        var secondId = await SeedAsync(ScheduleJobState.Pending, true);
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        var first = await db.ScheduleJob.FindAsync(firstId);
        #pragma warning disable EF1002 // テストで生成したULIDだけをトリガー条件へ埋め込む。
        await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER reject_reserve_delete BEFORE DELETE ON ScheduleJob WHEN OLD.Id = '{firstId}' BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        #pragma warning restore EF1002
        var logic = scope.ServiceProvider.GetRequiredService<ReserveLobLogic>();
        Assert.That((await logic.DeleteProgramReserveEntryAsync(firstId)).IsSuccess, Is.False);
        Assert.That(db.Entry(first!).State, Is.EqualTo(EntityState.Unchanged));
        Assert.That((await logic.DeleteProgramReserveEntryAsync(secondId)).IsSuccess, Is.True);
        Assert.That(await db.ScheduleJob.AsNoTracking().AnyAsync(x => x.Id == firstId), Is.True);
        Assert.That(await db.ScheduleJob.AsNoTracking().AnyAsync(x => x.Id == secondId), Is.False);
    }

    [TestCase(ScheduleJobState.Pending)]
    [TestCase(ScheduleJobState.Cancelled)]
    [TestCase(ScheduleJobState.Failed)]
    [TestCase(ScheduleJobState.Preparing)]
    public async Task 明示的な再有効化は中断状態をPendingへ戻す(ScheduleJobState state)
    {
        var id = await SeedAsync(state, false);
        using var scope = _provider.CreateScope();
        Assert.That((await scope.ServiceProvider.GetRequiredService<ReserveLobLogic>().SwitchKeywordReserveEntryStatusAsync(id)).IsSuccess, Is.True);
        var job = await ReadAsync(id);
        Assert.Multiple(() =>
        {
            Assert.That(job.IsEnabled, Is.True);
            Assert.That(job.State, Is.EqualTo(ScheduleJobState.Pending));
            Assert.That(job.CompletedUtc, Is.Null);
            Assert.That(job.LastErrorCode, Is.EqualTo(ScheduleJobErrorCode.None));
            Assert.That(job.PrepareStartUtc, Is.GreaterThan(DateTimeOffset.UtcNow));
        });
    }

    [TestCase(ScheduleJobState.Completed)]
    [TestCase(ScheduleJobState.Finalizing)]
    public async Task 完了済みと確定待ちは再投入しない(ScheduleJobState state)
    {
        var id = await SeedAsync(state, false);
        using var scope = _provider.CreateScope();
        Assert.That((await scope.ServiceProvider.GetRequiredService<ReserveLobLogic>().SwitchKeywordReserveEntryStatusAsync(id)).IsSuccess, Is.False);
        Assert.That((await ReadAsync(id)).State, Is.EqualTo(state));
        Assert.That((await ReadAsync(id)).IsEnabled, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task 復旧情報が残る場合は重複録音を防ぐ(bool corrupt)
    {
        var id = await SeedAsync(ScheduleJobState.Failed, false);
        var journal = _provider.GetRequiredService<RecordingFinalizationJournal>();
        journal.Write(new(Ulid.NewUlid(), id.ToString(), new MediaPath(Path.Combine(_root, "temp"), Path.Combine(_root, "audio.m4a"), "audio.m4a")));
        if (corrupt) await File.WriteAllTextAsync(journal.GetPendingFiles().Single(), "broken");
        using var scope = _provider.CreateScope();
        Assert.That((await scope.ServiceProvider.GetRequiredService<ReserveLobLogic>().SwitchKeywordReserveEntryStatusAsync(id)).IsSuccess, Is.False);
        Assert.That((await ReadAsync(id)).IsEnabled, Is.False);
        Assert.That(journal.GetPendingFiles().Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task 準備中の無効化は終了保存を待ち再有効化できる()
    {
        var id = await SeedAsync(ScheduleJobState.Queued, true);
        using var executionScope = _provider.CreateScope();
        var execution = executionScope.ServiceProvider.GetRequiredService<RecordingJobExecutor>().ExecuteAsync(id, default).AsTask();
        Assert.That((await ReadAsync(id)).State, Is.EqualTo(ScheduleJobState.Preparing));
        using var commandScope = _provider.CreateScope();
        var commands = commandScope.ServiceProvider.GetRequiredService<ReserveLobLogic>();
        Assert.That((await commands.SwitchKeywordReserveEntryStatusAsync(id)).IsSuccess, Is.True);
        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        var stopped = await ReadAsync(id);
        Assert.That(stopped.State, Is.EqualTo(ScheduleJobState.Cancelled));
        Assert.That(stopped.CompletedUtc, Is.Not.Null);
        Assert.That((await commands.SwitchKeywordReserveEntryStatusAsync(id)).IsSuccess, Is.True);
        Assert.That((await ReadAsync(id)).State, Is.EqualTo(ScheduleJobState.Pending));
    }

    [Test]
    public async Task 起動用初期化は非Pendingを成功扱いせず状態を保全する()
    {
        var id = await SeedAsync(ScheduleJobState.Cancelled, false);
        var job = await ReadAsync(id);
        using var scope = _provider.CreateScope();
        Assert.That((await scope.ServiceProvider.GetRequiredService<RecordJobLobLogic>().SetScheduleJobAsync(job)).IsSuccess, Is.False);
        Assert.That((await ReadAsync(id)).State, Is.EqualTo(ScheduleJobState.Cancelled));
    }

    private async Task<Ulid> SeedAsync(ScheduleJobState state, bool enabled)
    {
        var id = Ulid.NewUlid();
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
        db.ScheduleJob.Add(new ScheduleJob
        {
            Id = id, ProgramId = "p", ServiceKind = RadioServiceKind.Radiko, State = state, IsEnabled = enabled,
            RecordingType = RecordingType.RealTime, StartDateTime = DateTimeOffset.UtcNow.AddHours(1),
            EndDateTime = DateTimeOffset.UtcNow.AddHours(2), CompletedUtc = DateTimeOffset.UtcNow,
            LastErrorCode = ScheduleJobErrorCode.Cancelled
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<ScheduleJob> ReadAsync(Ulid id)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RadioDbContext>().ScheduleJob.AsNoTracking().SingleAsync(x => x.Id == id);
    }
}
