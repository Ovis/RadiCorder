using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.DependencyInjection;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;
using RadiCorder.Logics.UseCases.Recording;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RecordingFinalizationTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task 保存後のDB障害は再移動せず再起動後に確定できる(bool failPathUpdate, bool scheduled)
    {
        var root = Path.Combine(Path.GetTempPath(), $"radi-finalization-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var config = new Mock<IAppConfigurationService>();
            config.SetupGet(x => x.RecordFileSaveDir).Returns(root);
            config.SetupGet(x => x.TemporaryFileSaveDir).Returns(root);
            var options = new DbContextOptionsBuilder<RadioDbContext>().UseSqlite($"Data Source={Path.Combine(root, "test.db")}").Options;
            await using var db = new RadioDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var jobId = Ulid.NewUlid();
            if (scheduled)
            {
                db.ScheduleJob.Add(new ScheduleJob { Id = jobId, ProgramId = "p", ServiceKind = RadioServiceKind.Radiko, State = ScheduleJobState.Recording, IsEnabled = true });
                await db.SaveChangesAsync();
            }
            var realRepo = new RecordingRepository(NullLogger<RecordingRepository>.Instance, db);
            var repository = new Mock<IRecordingRepository>();
            repository.Setup(x => x.CreateAsync(It.IsAny<ProgramRecordingInfo>(), It.IsAny<MediaPath>(), It.IsAny<RecordingOptions>(), It.IsAny<CancellationToken>()))
                .Returns((ProgramRecordingInfo info, MediaPath path, RecordingOptions recordingOptions, CancellationToken ct) => realRepo.CreateAsync(info, path, recordingOptions, ct));
            repository.Setup(x => x.UpdateFilePathAsync(It.IsAny<Ulid>(), It.IsAny<MediaPath>(), It.IsAny<CancellationToken>()))
                .Returns((Ulid id, MediaPath path, CancellationToken ct) => failPathUpdate ? ValueTask.FromException(new IOException("DBパス更新失敗")) : realRepo.UpdateFilePathAsync(id, path, ct));
            repository.Setup(x => x.UpdateStateAsync(It.IsAny<Ulid>(), It.IsAny<RecordingState>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns((Ulid id, RecordingState state, string? message, CancellationToken ct) => state == RecordingState.Completed ? ValueTask.FromException(new IOException("DB完了更新失敗")) : realRepo.UpdateStateAsync(id, state, message, ct));

            var journal = new RecordingFinalizationJournal(config.Object);
            var storage = new MediaStorageService(config.Object, finalizationJournal: journal);
            var storageProxy = new Mock<IRecoverableMediaStorageService>();
            var tempPath = Path.Combine(root, "audio.tmp");
            var mediaPath = new MediaPath(tempPath, Path.Combine(root, "audio.m4a"), "audio.m4a");
            await File.WriteAllTextAsync(mediaPath.FinalFilePath, "前回録音");
            storageProxy.Setup(x => x.PrepareAsync(It.IsAny<ProgramRecordingInfo>(), It.IsAny<RecordingOptions>(), It.IsAny<CancellationToken>())).ReturnsAsync(mediaPath);
            storageProxy.Setup(x => x.CommitRecordingAsync(It.IsAny<MediaPath>(), It.IsAny<Ulid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Returns((MediaPath path, Ulid id, string? job, CancellationToken ct) => storage.CommitRecordingAsync(path, id, job, ct));
            var info = new ProgramRecordingInfo("p", "番組", "", "TBS", "TBS", "JP13", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1), "", "", "");
            var source = new Mock<IRecordingSource>();
            source.Setup(x => x.CanHandle(RadioServiceKind.Radiko)).Returns(true);
            source.Setup(x => x.PrepareAsync(It.IsAny<RecordingCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordingSourceResult("https://example/stream", new Dictionary<string, string>(), info, new RecordingOptions(RadioServiceKind.Radiko, false, 0, 0)));
            var transcode = new Mock<IMediaTranscodeService>();
            transcode.Setup(x => x.RecordAsync(It.IsAny<RecordingSourceResult>(), It.IsAny<MediaPath>(), It.IsAny<CancellationToken>())).Returns(async () => { await File.WriteAllTextAsync(tempPath, "今回録音"); return true; });
            var orchestrator = new RecordingOrchestrator(NullLogger<RecordingOrchestrator>.Instance, [source.Object], storageProxy.Object, transcode.Object, repository.Object, Mock.Of<IRecordingStateEventPublisher>());
            var result = await orchestrator.RecordAsync(new RecordingCommand(RadioServiceKind.Radiko, "p", "番組", false, 0, 0, ScheduleJobId: scheduled ? jobId.ToString() : null));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("保存済み"));
            storageProxy.Verify(x => x.SaveFailedAsync(It.IsAny<MediaPath>(), It.IsAny<SaveFailedFallbackMetadata>(), It.IsAny<CancellationToken>()), Times.Never);
            var entry = journal.Read(journal.GetPendingFiles().Single());
            Assert.That(File.Exists(entry.Path.FinalFilePath), Is.True);
            Assert.That(File.ReadAllText(mediaPath.FinalFilePath), Is.EqualTo("前回録音"));

            // 別スコープのDBで復旧し、重複名を含む実際の保存先を反映する。
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddRadiCorderLogics();
            services.AddSingleton<IAppConfigurationService>(config.Object);
            services.AddSingleton(Mock.Of<IRecordingStateEventPublisher>());
            services.AddSingleton(Mock.Of<IRadikoProxyTicketService>());
            services.AddSingleton(Mock.Of<ILocalApplicationUrlService>());
            services.AddSingleton(Mock.Of<IFfmpegService>());
            services.AddDbContext<RadioDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(root, "test.db")}"));
            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RecordingFinalizationRecovery>().RecoverAsync(default);
            var recoveredDb = scope.ServiceProvider.GetRequiredService<RadioDbContext>();
            Assert.That((await recoveredDb.Recordings.SingleAsync()).State, Is.EqualTo(RecordingState.Completed));
            Assert.That((await recoveredDb.RecordingFiles.SingleAsync()).FileRelativePath, Is.EqualTo(entry.Path.RelativePath));
            if (scheduled)
            {
                var recoveredJob = await recoveredDb.ScheduleJob.AsNoTracking().SingleAsync();
                Assert.That(recoveredJob.State, Is.EqualTo(ScheduleJobState.Completed));
                Assert.That(recoveredJob.IsEnabled, Is.False);
            }
            Assert.That(journal.GetPendingFiles(), Is.Empty);
            await scope.ServiceProvider.GetRequiredService<RecordingFinalizationRecovery>().RecoverAsync(default);
            Assert.That(await recoveredDb.Recordings.CountAsync(), Is.EqualTo(1));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}
