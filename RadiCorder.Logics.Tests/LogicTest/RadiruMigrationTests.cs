using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.RdbContext;

namespace RadiCorder.Logics.Tests.LogicTest;

/// <summary>
/// らじる再編関連マイグレーションのテスト
/// </summary>
public class RadiruMigrationTests
{
    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"radicorder-radiru-migration-{Guid.NewGuid():N}.db");

    [Test]
    public async Task MigrateAsync_最新マイグレーションまで適用できる()
    {
        var dbPath = CreateDatabasePath();
        try
        {
            var options = new DbContextOptionsBuilder<RadioDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using var dbContext = new RadioDbContext(options);
            await dbContext.Database.EnsureDeletedAsync();

            await dbContext.Database.MigrateAsync();

            var canAccessAreas = await dbContext.NhkRadiruAreas.AnyAsync();
            var canAccessAreaServices = await dbContext.NhkRadiruAreaServices.AnyAsync();

            Assert.That(canAccessAreas, Is.False);
            Assert.That(canAccessAreaServices, Is.False);
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    [Test]
    public async Task MigrateAsync_従来局テーブルから現行定義へバックフィルされる()
    {
        var dbPath = CreateDatabasePath();
        try
        {
            var options = new DbContextOptionsBuilder<RadioDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            await using var dbContext = new RadioDbContext(options);
            await dbContext.Database.EnsureDeletedAsync();

            await dbContext.Database.MigrateAsync("20260227154835_Initialize");

            await dbContext.Database.ExecuteSqlRawAsync("""
                INSERT INTO NhkRadiruStations
                (AreaId, AreaJpName, ApiKey, R1Hls, R2Hls, FmHls, ProgramNowOnAirApiUrl, ProgramDetailApiUrlTemplate, DailyProgramApiUrlTemplate)
                VALUES
                ('130', '東京', '130', 'https://example/r1.m3u8', '', 'https://example/r3.m3u8', 'https://example/noa/130', 'https://example/detail/{{area}}', 'https://example/day/{{area}}');
                """);

            await dbContext.Database.MigrateAsync();

            var area = await dbContext.NhkRadiruAreas.SingleAsync(x => x.AreaId == "130");
            var services = await dbContext.NhkRadiruAreaServices
                .Where(x => x.AreaId == "130")
                .OrderBy(x => x.ServiceId)
                .ToListAsync();

            Assert.That(area.AreaJpName, Is.EqualTo("東京"));
            Assert.That(area.DailyProgramApiUrlTemplate, Is.EqualTo("https://example/day/{area}"));
            Assert.That(services.Select(x => x.ServiceId).ToArray(), Is.EqualTo(new[] { "r1", "r3" }));
            Assert.That(services.Any(x => x.ServiceId == "r2"), Is.False);
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
