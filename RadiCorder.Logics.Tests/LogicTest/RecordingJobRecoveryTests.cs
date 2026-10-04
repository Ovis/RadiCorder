using Microsoft.EntityFrameworkCore;
using Moq;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RecordingJobRecoveryTests
{
    [Test]
    public async Task 放送開始が古いタイムフリーも取得開始が直近なら再投入する()
    {
        await using var db = new RadioDbContext(new DbContextOptionsBuilder<RadioDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        db.ScheduleJob.Add(new ScheduleJob { Id = Ulid.NewUlid(), ProgramId = "p", ServiceKind = RadioServiceKind.Radiko, State = ScheduleJobState.Recording, RecordingType = RecordingType.TimeFree, IsEnabled = true, StartDateTime = now.AddHours(-4), EndDateTime = now.AddHours(-3), ActualStartUtc = now.AddMinutes(-1) });
        await db.SaveChangesAsync();
        await new RecordingJobRecovery(db, Mock.Of<IAppConfigurationService>()).RecoverAsync(now, default);
        Assert.That((await db.ScheduleJob.SingleAsync()).State, Is.EqualTo(ScheduleJobState.Pending));
    }

    [TestCase(ScheduleJobState.Queued, 3, ScheduleJobState.Failed, false)]
    [TestCase(ScheduleJobState.Recording, 3, ScheduleJobState.Failed, false)]
    [TestCase(ScheduleJobState.Finalizing, 1, ScheduleJobState.Failed, false)]
    [TestCase(ScheduleJobState.Completed, 1, ScheduleJobState.Completed, false)]
    [TestCase(ScheduleJobState.Failed, 1, ScheduleJobState.Failed, false)]
    [TestCase(ScheduleJobState.Cancelled, 1, ScheduleJobState.Cancelled, false)]
    [TestCase(ScheduleJobState.Queued, 1, ScheduleJobState.Pending, true)]
    [TestCase(ScheduleJobState.Preparing, 1, ScheduleJobState.Pending, true)]
    public async Task 中断状態を保持して再投入か失敗を判断する(ScheduleJobState state, int hours, ScheduleJobState expected, bool enabled)
    {
        await using var db = new RadioDbContext(new DbContextOptionsBuilder<RadioDbContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var now = DateTimeOffset.UtcNow;
        var job = new ScheduleJob { Id = Ulid.NewUlid(), ProgramId = "p", ServiceKind = RadioServiceKind.Radiko, State = state, RecordingType = RecordingType.TimeFree, IsEnabled = true, StartDateTime = now.AddHours(-hours), EndDateTime = now.AddHours(-hours).AddMinutes(30), ActualStartUtc = now.AddHours(-hours) };
        db.ScheduleJob.Add(job);
        await db.SaveChangesAsync();
        await new RecordingJobRecovery(db, Mock.Of<IAppConfigurationService>()).RecoverAsync(now, default);
        db.ChangeTracker.Clear();
        var result = await db.ScheduleJob.SingleAsync();
        Assert.That(result.State, Is.EqualTo(expected));
        Assert.That(result.IsEnabled, Is.EqualTo(enabled));
        if (!enabled) Assert.That(result.ActualStartUtc, Is.EqualTo(job.ActualStartUtc));
        await new RecordingJobRecovery(db, Mock.Of<IAppConfigurationService>()).RecoverAsync(now, default);
        Assert.That((await db.ScheduleJob.SingleAsync()).State, Is.EqualTo(expected));
    }
}
