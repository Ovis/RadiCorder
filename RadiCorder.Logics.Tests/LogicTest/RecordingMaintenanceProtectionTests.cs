using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Infrastructure.Recording;
using RadiCorder.Logics.Logics;
using RadiCorder.Logics.RdbContext;
using RadiCorder.Logics.Services;

namespace RadiCorder.Logics.Tests.LogicTest;

public class RecordingMaintenanceProtectionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task 確定待ちや読み取れない復旧情報があれば古い作業ファイルを保持する(bool brokenJournal)
    {
        var root = Path.Combine(Path.GetTempPath(), $"radi-maintenance-{Guid.NewGuid():N}");
        var work = TemporaryStoragePaths.GetRecordingsWorkDirectory(root);
        Directory.CreateDirectory(work);
        try
        {
            var config = new Mock<IAppConfigurationService>();
            config.SetupGet(x => x.RecordFileSaveDir).Returns(root);
            config.SetupGet(x => x.TemporaryFileSaveDir).Returns(root);
            var temp = Path.Combine(work, "audio.m4a");
            await File.WriteAllTextAsync(temp, "録音データ");
            File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddDays(-40));
            var journal = new RecordingFinalizationJournal(config.Object);
            journal.Write(new(Ulid.NewUlid(), null, new MediaPath(temp, Path.Combine(root, "audio.m4a"), "audio.m4a")));
            if (brokenJournal) await File.WriteAllTextAsync(journal.GetPendingFiles().Single(), "broken");
            await using var db = new RadioDbContext(new DbContextOptionsBuilder<RadioDbContext>().UseSqlite("Data Source=:memory:").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
            await new TemporaryStorageMaintenanceLobLogic(NullLogger<TemporaryStorageMaintenanceLobLogic>.Instance, config.Object, db).CleanupAsync();
            Assert.That(File.Exists(temp), Is.True);
            Assert.That(journal.GetPendingFiles().Count(), Is.EqualTo(1));
        }
        finally { Directory.Delete(root, true); }
    }
}
