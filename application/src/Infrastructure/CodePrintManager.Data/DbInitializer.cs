using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CodePrintManager.Data;

public static class DbInitializer
{
    /// <summary>
    /// Applies pending migrations and configures SQLite for concurrent multi-job writes.
    /// Call once at startup. This is the single entry point for DB initialization.
    /// </summary>
    public static async Task InitializeAsync(AppDbContext context)
    {
        // Backup existing database before migration (WAL-safe via VACUUM INTO)
        await BackupDatabaseAsync(context);

        await context.Database.MigrateAsync();

        // Enable WAL mode and set busy timeout for concurrent multi-job writes
        var connection = context.Database.GetDbConnection();
        if (connection is SqliteConnection sqliteConn)
        {
            var wasOpen = sqliteConn.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await sqliteConn.OpenAsync();

            await using var cmd = sqliteConn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            await cmd.ExecuteNonQueryAsync();

            // Verify WAL mode is active
            cmd.CommandText = "PRAGMA journal_mode;";
            var journalMode = (await cmd.ExecuteScalarAsync())?.ToString();
            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"WARNING: SQLite journal_mode is '{journalMode}', expected 'wal'. Concurrent writes may fail.");
            }

            // Quick integrity check
            cmd.CommandText = "PRAGMA quick_check;";
            var integrityResult = (await cmd.ExecuteScalarAsync())?.ToString();
            if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"DATABASE INTEGRITY WARNING: {integrityResult}");
            }

            if (!wasOpen) await sqliteConn.CloseAsync();
        }
    }

    private static async Task BackupDatabaseAsync(AppDbContext context)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            if (connection is not SqliteConnection sqliteConn) return;

            var wasOpen = sqliteConn.State == System.Data.ConnectionState.Open;
            if (!wasOpen) await sqliteConn.OpenAsync();

            var dbPath = sqliteConn.DataSource;
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                if (!wasOpen) await sqliteConn.CloseAsync();
                return;
            }

            var backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
            Directory.CreateDirectory(backupDir);
            var backupName = $"{Path.GetFileNameWithoutExtension(dbPath)}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db";
            var backupPath = Path.Combine(backupDir, backupName);

            await using var cmd = sqliteConn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
            await cmd.ExecuteNonQueryAsync();

            // Keep only the last 5 backups
            var oldBackups = Directory.GetFiles(backupDir, "*.db")
                .OrderByDescending(f => f).Skip(5);
            foreach (var f in oldBackups)
                try { File.Delete(f); } catch { /* best effort */ }

            if (!wasOpen) await sqliteConn.CloseAsync();
        }
        catch (Exception ex)
        {
            // Backup failure should not prevent app startup
            Console.Error.WriteLine($"WARNING: Database backup failed: {ex.Message}");
        }
    }
}
