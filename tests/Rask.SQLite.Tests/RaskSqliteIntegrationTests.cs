using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Tests;

// Integration tests against a real SQLite database file: they prove the pragmas actually take effect
// on a live connection — journal_mode really is WAL, foreign_keys really is enforced, the busy_timeout
// really is set — through both the raw-ADO factory and the Entity Framework Core interceptor.
public sealed class RaskSqliteIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Factory_connection_reports_the_configured_pragmas()
    {
        var services = new ServiceCollection();
        services.AddRaskSqlite($"Data Source={_dbPath}");
        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRaskSqliteConnectionFactory>();

        await using var connection = await factory.CreateOpenAsync();

        Assert.Equal("wal", ReadPragma(connection, "journal_mode"));
        Assert.Equal("1", ReadPragma(connection, "foreign_keys"));
        Assert.Equal("5000", ReadPragma(connection, "busy_timeout"));
        Assert.Equal("2000", ReadPragma(connection, "cache_size"));
    }

    [Fact]
    public async Task Ef_interceptor_reports_the_configured_pragmas_on_open()
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite($"Data Source={_dbPath}")
            .Options;

        await using var context = new ProbeDbContext(options);
        await context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        Assert.Equal("wal", ReadPragma(connection, "journal_mode"));
        Assert.Equal("1", ReadPragma(connection, "foreign_keys"));
        Assert.Equal("5000", ReadPragma(connection, "busy_timeout"));

        await context.Database.CloseConnectionAsync();
    }

    [Fact]
    public async Task Ef_interceptor_honors_pragma_overrides()
    {
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite($"Data Source={_dbPath}", p => p.BusyTimeout = TimeSpan.FromSeconds(12))
            .Options;

        await using var context = new ProbeDbContext(options);
        await context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)context.Database.GetDbConnection();

        Assert.Equal("12000", ReadPragma(connection, "busy_timeout"));

        await context.Database.CloseConnectionAsync();
    }

    private static string ReadPragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }
    }
}
