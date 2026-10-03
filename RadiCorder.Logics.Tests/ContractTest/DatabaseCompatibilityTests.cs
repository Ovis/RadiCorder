using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.Domain.Recording;
using RadiCorder.Logics.Models.Enums;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.ContractTest;

/// <summary>
/// リファクタリング前のDB契約と保存済みデータの読み戻しを確認する
/// </summary>
public class DatabaseCompatibilityTests
{
    [Test]
    public async Task MigrateAsync_既存データと関連と時刻を再起動後も保持する()
    {
        var root = Path.Combine(Path.GetTempPath(), $"radicorder-db-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var options = new DbContextOptionsBuilder<RadioDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "radio.db")};Pooling=False")
            .Options;
        var recordingId = Ulid.NewUlid();
        var reserveId = Ulid.NewUlid();
        var jobId = Ulid.NewUlid();
        var tagId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 4, 1, 5, 0, 0, TimeSpan.FromHours(9));
        try
        {
            await using (var db = new RadioDbContext(options))
            {
                await db.Database.MigrateAsync();
                Assert.That(db.Database.HasPendingModelChanges(), Is.False);
                Assert.That(await db.Database.GetAppliedMigrationsAsync(), Is.EqualTo(new[]
                {
                    "20260227154835_Initialize",
                    "20260309041359_AddRadiruAreaTables",
                    "20260309072051_DropLegacyNhkRadiruStations",
                    "20260330151254_AddRadikoStationActiveFlags"
                }));
                db.Recordings.Add(new Recording
                {
                    Id = recordingId, ServiceKind = RadioServiceKind.Radiko, ProgramId = "program",
                    StationId = "station", StartDateTime = start, EndDateTime = start.AddHours(1),
                    State = RecordingState.Completed, CreatedAt = start, UpdatedAt = start,
                    RecordingFile = new RecordingFile { RecordingId = recordingId, FileRelativePath = "局/番組.m4a" },
                    RecordingMetadata = new RecordingMetadata { RecordingId = recordingId, Title = "保存済み番組", StationName = "局" }
                });
                db.RecordingTags.Add(new RecordingTag
                {
                    Id = tagId, Name = "タグ", NormalizedName = "タグ", CreatedAt = start, UpdatedAt = start
                });
                db.RecordingTagRelations.Add(new RecordingTagRelation { RecordingId = recordingId, TagId = tagId });
                db.KeywordReserve.Add(new KeywordReserve { Id = reserveId, Keyword = "番組", SortOrder = 2 });
                db.ScheduleJob.Add(new ScheduleJob
                {
                    Id = jobId, KeywordReserveId = reserveId, ProgramId = "pending-program", StationId = "station",
                    ServiceKind = RadioServiceKind.Radiko, ReserveType = ReserveType.Keyword,
                    RecordingType = RecordingType.RealTime, StartDateTime = start, EndDateTime = start.AddHours(1),
                    PrepareStartUtc = start.AddSeconds(-11), QueuedAtUtc = start.AddSeconds(-10), IsEnabled = true
                });
                db.ScheduleJobKeywordReserveRelations.Add(new ScheduleJobKeywordReserveRelation
                {
                    ScheduleJobId = jobId, KeywordReserveId = reserveId
                });
                db.AppConfigurations.Add(new AppConfiguration { ConfigurationName = "unchanged-key", Val1 = "unchanged-value", Val2 = 7 });
                await db.SaveChangesAsync();
            }

            await using (var db = new RadioDbContext(options))
            {
                // 現行DBに再度起動処理を適用しても、migrationやデータを書き換えない。
                await db.Database.MigrateAsync();
                Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
                var recording = await db.Recordings.Include(x => x.RecordingFile).Include(x => x.RecordingMetadata).SingleAsync();
                var job = await db.ScheduleJob.Include(x => x.KeywordReserveRelations).SingleAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(recording.Id, Is.EqualTo(recordingId));
                    Assert.That(recording.State, Is.EqualTo(RecordingState.Completed));
                    Assert.That(recording.StartDateTime, Is.EqualTo(start.ToUniversalTime()));
                    Assert.That(recording.StartDateTime.Offset, Is.EqualTo(TimeSpan.Zero));
                    Assert.That(recording.RecordingFile!.FileRelativePath, Is.EqualTo("局/番組.m4a"));
                    Assert.That(recording.RecordingMetadata!.Title, Is.EqualTo("保存済み番組"));
                    Assert.That(job.QueuedAtUtc, Is.EqualTo(start.AddSeconds(-10).ToUniversalTime()));
                    Assert.That(job.State, Is.EqualTo(ScheduleJobState.Pending));
                    Assert.That(job.KeywordReserveRelations.Single().KeywordReserveId, Is.EqualTo(reserveId));
                });
                Assert.That((await db.RecordingTagRelations.SingleAsync()).TagId, Is.EqualTo(tagId));
                Assert.That((await db.AppConfigurations.SingleAsync()).Val1, Is.EqualTo("unchanged-value"));
                await db.Database.OpenConnectionAsync();
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "PRAGMA integrity_check";
                Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("ok"));
                command.CommandText = "PRAGMA foreign_key_check";
                await using var reader = await command.ExecuteReaderAsync();
                Assert.That(await reader.ReadAsync(), Is.False);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
