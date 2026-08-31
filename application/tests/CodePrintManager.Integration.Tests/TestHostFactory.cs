using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace CodePrintManager.Integration.Tests;

public class TestHostFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;
    private readonly bool _ownsDb;

    /// <summary>
    /// Creates a TestHostFactory with a fresh temp DB (default).
    /// </summary>
    public TestHostFactory()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"cpm_test_{Guid.NewGuid():N}.db");
        _ownsDb = true;
    }

    /// <summary>
    /// Creates a TestHostFactory using an external DB path.
    /// Used for crash-recovery tests where two hosts share the same DB.
    /// The caller is responsible for cleanup when ownsDb is false.
    /// </summary>
    public TestHostFactory(string dbPath, bool ownsDb = false)
    {
        _dbPath = dbPath;
        _ownsDb = ownsDb;
    }

    /// <summary>The DB path used by this factory (for sharing between instances).</summary>
    public string DbPath => _dbPath;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DbPath"] = _dbPath
            });
        });

        return base.CreateHost(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsDb && File.Exists(_dbPath))
        {
            // Clear SQLite connection pool so file handles are released
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch { /* best effort */ }
            try { File.Delete(_dbPath + "-wal"); } catch { }
            try { File.Delete(_dbPath + "-shm"); } catch { }
        }
    }
}
