using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.Limitations.Tests;

public sealed class Price
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
}

public sealed class PriceContext(string dbPath) : DbContext
{
    public DbSet<Price> Prices => Set<Price>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite($"Data Source={dbPath}");
}

// These tests PIN the SQLite limitations documented in docs/sqlite.md — they demonstrate the real
// behavior (not Rask code), so the "when to outgrow SQLite" guidance stays honest and can't silently drift.
public sealed class SqliteLimitationsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-limits-{Guid.NewGuid():N}.db");

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        return connection;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Dynamic_typing_a_text_value_fits_an_integer_column()
    {
        using var db = Open();
        Exec(db, "CREATE TABLE n (x INTEGER);");
        Exec(db, "INSERT INTO n (x) VALUES ('lots');"); // not enforced — affinity can't coerce it

        Assert.Equal("lots", Scalar(db, "SELECT x FROM n"));
        Assert.Equal("text", Scalar(db, "SELECT typeof(x) FROM n")); // stored as TEXT, no error
    }

    [Fact]
    public void No_native_decimal_real_arithmetic_is_inexact()
    {
        using var db = Open();
        // REAL money loses precision — the classic 0.1 + 0.2 != 0.3. (This is why EF Core avoids REAL.)
        Assert.Equal(0L, Scalar(db, "SELECT (0.1 + 0.2 = 0.3)")); // false
        Assert.NotEqual(0.3, Convert.ToDouble(Scalar(db, "SELECT 0.1 + 0.2")));
    }

    [Fact]
    public void EF_Core_stores_decimal_as_text_to_keep_precision()
    {
        using (var ctx = new PriceContext(_dbPath))
        {
            ctx.Database.EnsureCreated();
            ctx.Prices.Add(new Price { Amount = 19.95m });
            ctx.SaveChanges();
        }

        // EF Core stores decimal as TEXT (not REAL), so the value round-trips exactly...
        using var read = new PriceContext(_dbPath);
        Assert.Equal(19.95m, read.Prices.Single().Amount);

        // ...but the raw column is TEXT — numeric ORDER BY / SUM don't translate on it.
        using var raw = Open();
        Assert.Equal("text", Scalar(raw, "SELECT typeof(Amount) FROM Prices"));
    }

    [Fact]
    public void DateTime_and_Guid_are_stored_as_text()
    {
        using var db = Open();
        Exec(db, "CREATE TABLE e (d, g);");
        using (var insert = db.CreateCommand())
        {
            insert.CommandText = "INSERT INTO e (d, g) VALUES ($d, $g)";
            insert.Parameters.AddWithValue("$d", DateTime.UtcNow);
            insert.Parameters.AddWithValue("$g", Guid.NewGuid());
            insert.ExecuteNonQuery();
        }

        Assert.Equal("text", Scalar(db, "SELECT typeof(d) FROM e"));
        Assert.Equal("text", Scalar(db, "SELECT typeof(g) FROM e"));
    }

    [Fact]
    public void Single_writer_a_second_immediate_transaction_is_busy()
    {
        using var writer = Open();
        Exec(writer, "PRAGMA busy_timeout = 0;");
        Exec(writer, "CREATE TABLE t (x);");
        Exec(writer, "BEGIN IMMEDIATE;"); // takes the write lock and holds it (no commit)

        using var second = Open();
        Exec(second, "PRAGMA busy_timeout = 0;");

        using var begin = second.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE;";
        begin.CommandTimeout = 1; // cap the driver's SQLITE_BUSY retry (0 would mean retry forever)

        var ex = Assert.Throws<SqliteException>(() => begin.ExecuteNonQuery());
        Assert.Equal(5, ex.SqliteErrorCode); // SQLITE_BUSY — writes serialize to one writer

        Exec(writer, "ROLLBACK;");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
