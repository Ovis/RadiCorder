using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RadiCorder.Hosting;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Web.Tests;

public class DatabaseInitializerTests
{
    [Test]
    public async Task Initialize_旧DBのWALデータをバックアップし最新DBの再起動では変更しない()
    {
        var root = Path.Combine(Path.GetTempPath(), $"radicorder-db-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "radio.db");
        var options = new DbContextOptionsBuilder<RadioDbContext>().UseSqlite($"Data Source={dbPath};Pooling=False").Options;
        try
        {
            await using (var db = new RadioDbContext(options))
            {
                await db.Database.MigrateAsync("20260227154835_Initialize");
                await db.Database.OpenConnectionAsync();
                await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL");
                db.AppConfigurations.Add(new AppConfiguration { ConfigurationName = "retained-value", Val1 = "保存済み設定" });
                await db.SaveChangesAsync();
                await DatabaseInitializer.ApplyMigrationsWithBackupIfNeededAsync(db, root, NullLogger.Instance);
                Assert.That(await db.Database.GetPendingMigrationsAsync(), Is.Empty);
                Assert.That((await db.AppConfigurations.SingleAsync()).Val1, Is.EqualTo("保存済み設定"));
            }
            var backupDir = Path.Combine(root, "migration-backups");
            var backup = Directory.GetFiles(backupDir, "*.db").Single();
            await using (var connection = new SqliteConnection($"Data Source={backup};Mode=ReadOnly;Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT Val1 FROM AppConfigurations WHERE ConfigurationName = 'retained-value'";
                Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("保存済み設定"));
                command.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory";
                Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo(1L));
                command.CommandText = "PRAGMA integrity_check";
                Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo("ok"));
            }
            var before = await File.ReadAllBytesAsync(backup);
            await using (var db = new RadioDbContext(options))
            {
                await DatabaseInitializer.ApplyMigrationsWithBackupIfNeededAsync(db, root, NullLogger.Instance);
            }
            Assert.That(Directory.GetFiles(backupDir, "*.db"), Has.Length.EqualTo(1));
            Assert.That(await File.ReadAllBytesAsync(backup), Is.EqualTo(before));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
