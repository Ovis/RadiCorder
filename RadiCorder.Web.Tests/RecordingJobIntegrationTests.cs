using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Logics.RecordJobLogic;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Web.Tests;

/// <summary>
/// 本番DI、SQLite、file保存を通してジョブと録音結果の整合性を確認する。
/// </summary>
public class RecordingJobIntegrationTests
{
    [TestCase(RadioServiceKind.Radiko, RecordingType.Immediate, true)]
    [TestCase(RadioServiceKind.Radiko, RecordingType.TimeFree, true)]
    [TestCase(RadioServiceKind.Radiru, RecordingType.Immediate, true)]
    [TestCase(RadioServiceKind.Radiru, RecordingType.OnDemand, true)]
    [TestCase(RadioServiceKind.Radiko, RecordingType.TimeFree, false)]
    [TestCase(RadioServiceKind.Radiru, RecordingType.Immediate, false)]
    public async Task ExecuteAsync_ジョブの終了状態と録音fileを維持する(RadioServiceKind kind, RecordingType type, bool success)
    {
        var start = DateTimeOffset.Parse("2026-04-01T05:00:00+09:00");
        var info = new ProgramRecordingInfo("fixture-program", "番組", "", "station", "放送局", "130", start, start.AddHours(1), "出演者", "説明", "");
        var options = new RecordingOptions(kind, type == RecordingType.TimeFree, 0, 0, type == RecordingType.OnDemand);
        var prepared = new RecordingSourceResult("https://fixture.invalid/media.m3u8", new Dictionary<string, string>(), info, options);
        var source = new Mock<IRecordingSource>();
        source.Setup(x => x.CanHandle(kind)).Returns(true);
        source.Setup(x => x.PrepareAsync(It.IsAny<RecordingCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(prepared);
        var transcoder = new Mock<IMediaTranscodeService>();
        transcoder.Setup(x => x.RecordAsync(It.IsAny<RecordingSourceResult>(), It.IsAny<MediaPath>(), It.IsAny<CancellationToken>()))
            .Returns((RecordingSourceResult _, MediaPath path, CancellationToken token) => new ValueTask<bool>(WriteMediaAsync(path, success, token)));

        await using var host = new WebTestHost();
        await host.StartAsync(services =>
        {
            services.RemoveAll<IRecordingSource>();
            services.AddSingleton(source.Object);
            services.AddSingleton(transcoder.Object);
        });
        var jobId = Ulid.NewUlid();
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            db.ScheduleJob.Add(new ScheduleJob
            {
                Id = jobId, ProgramId = info.ProgramId, StationId = info.StationId, Title = info.Title,
                ServiceKind = kind, RecordingType = type, ReserveType = ReserveType.Program,
                StartDateTime = start, EndDateTime = start.AddHours(1), PrepareStartUtc = start,
                State = ScheduleJobState.Queued, IsEnabled = true
            });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<RecordingJobExecutor>().ExecuteAsync(jobId, CancellationToken.None);
        }

        // 新しいscopeから読み戻し、tracked entityの見かけだけで成功と判定しない。
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            var recording = await db.Recordings.Include(x => x.RecordingFile).SingleAsync();
            Assert.That(recording.State, Is.EqualTo(success ? RecordingState.Completed : RecordingState.Failed));
            Assert.That(recording.IsTimeFree, Is.EqualTo(type == RecordingType.TimeFree));
            Assert.That(await db.RecordingMetadatas.CountAsync(), Is.EqualTo(1));
            if (success)
            {
                Assert.That(await db.ScheduleJob.AnyAsync(x => x.Id == jobId), Is.False);
                var root = scope.ServiceProvider.GetRequiredService<IAppConfigurationService>().RecordFileSaveDir;
                Assert.That(await File.ReadAllBytesAsync(Path.Combine(root, recording.RecordingFile!.FileRelativePath)), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            }
            else
            {
                var job = await db.ScheduleJob.SingleAsync(x => x.Id == jobId);
                Assert.Multiple(() =>
                {
                    Assert.That(job.State, Is.EqualTo(ScheduleJobState.Failed));
                    Assert.That(job.IsEnabled, Is.False);
                    Assert.That(job.RetryCount, Is.EqualTo(1));
                    Assert.That(job.CompletedUtc, Is.Not.Null);
                });
            }
        }
        source.Verify(x => x.PrepareAsync(It.Is<RecordingCommand>(x => x.IsTimeFree == (type == RecordingType.TimeFree) && x.IsOnDemand == (type == RecordingType.OnDemand)), It.IsAny<CancellationToken>()),
            Times.Exactly(!success && type == RecordingType.TimeFree ? 2 : 1));
    }

    private static async Task<bool> WriteMediaAsync(MediaPath path, bool success, CancellationToken token)
    {
        await File.WriteAllBytesAsync(path.TempFilePath, [1, 2, 3, 4], token);
        return success;
    }
}
