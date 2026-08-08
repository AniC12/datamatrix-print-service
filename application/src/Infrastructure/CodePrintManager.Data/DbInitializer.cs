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
        await context.Database.MigrateAsync();

        // Enable WAL mode and set busy timeout for concurrent multi-job writes
        var connection = context.Database.GetDbConnection();
        if (connection is SqliteConnection sqliteConn)
        {
            await sqliteConn.OpenAsync();
            await using var cmd = sqliteConn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
