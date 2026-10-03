using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RadiCorder.Logics.RdbContext;
using ZLogger;

namespace RadiCorder.Hosting;

/// <summary>
/// 既存DBのバックアップとマイグレーションを行う。
/// </summary>
public static class DatabaseInitializer
{
    public static async Task ApplyMigrationsWithBackupIfNeededAsync(
        RadioDbContext context,
        string contentRootPath,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pendingMigrations.Length == 0)
        {
            return;
        }

        TryBackupSqliteDatabaseFile(context, contentRootPath, pendingMigrations, logger);

        logger.ZLogInformation($"DBマイグレーションを実行します。 pendingCount={pendingMigrations.Length}");
        await context.Database.MigrateAsync(cancellationToken);
        logger.ZLogInformation($"DBマイグレーションが完了しました。");
    }

    static void TryBackupSqliteDatabaseFile(
        RadioDbContext context,
        string contentRootPath,
        IReadOnlyCollection<string> pendingMigrations,
        ILogger logger)
    {
        var connectionString = context.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.ZLogWarning($"SQLite接続文字列が取得できなかったため、DBバックアップをスキップします。");
            return;
        }

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
        {
            logger.ZLogWarning($"SQLiteのData Sourceがファイルではないため、DBバックアップをスキップします。");
            return;
        }

        var dbFilePath = Path.IsPathRooted(dataSource)
            ? dataSource
            : Path.GetFullPath(dataSource, contentRootPath);

        if (!File.Exists(dbFilePath))
        {
            logger.ZLogInformation($"SQLite DBファイルが未作成のため、バックアップをスキップします。 path={dbFilePath}");
            return;
        }

        var backupDir = Path.Combine(Path.GetDirectoryName(dbFilePath)!, "migration-backups");
        Directory.CreateDirectory(backupDir);

        var latestMigration = pendingMigrations.LastOrDefault() ?? "unknown";
        var backupFileName = $"{Path.GetFileNameWithoutExtension(dbFilePath)}.{DateTimeOffset.Now:yyyyMMdd_HHmmss}.before_{latestMigration}.db";
        var backupPath = Path.Combine(backupDir, backupFileName);

        using var sourceConnection = new SqliteConnection(connectionString);
        sourceConnection.Open();

        using var backupConnection = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate");
        backupConnection.Open();

        sourceConnection.BackupDatabase(backupConnection);
        logger.ZLogInformation($"SQLite DBバックアップを作成しました。 path={backupPath}, pendingCount={pendingMigrations.Count}");

        PruneOldMigrationBackups(backupDir, Path.GetFileNameWithoutExtension(dbFilePath), keepCount: 2, logger);
    }

    static void PruneOldMigrationBackups(string backupDir, string dbFileBaseName, int keepCount, ILogger logger)
    {
        if (keepCount < 1)
        {
            keepCount = 1;
        }

        var pattern = $"{dbFileBaseName}.*.before_*.db";
        var files = new DirectoryInfo(backupDir)
            .GetFiles(pattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(x => x.CreationTimeUtc)
            .ToList();

        var deleteTargets = files.Skip(keepCount).ToList();
        foreach (var file in deleteTargets)
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                logger.ZLogWarning($"古いDBバックアップの削除に失敗しました。 path={file.FullName}, error={ex.Message}");
            }
        }
    }
}
