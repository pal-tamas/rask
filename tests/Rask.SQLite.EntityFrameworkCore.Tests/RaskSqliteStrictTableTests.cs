using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

// STRICT tables (SQLite 3.37+) make the store enforce each column's declared type instead of coercing
// whatever it is handed. EF Core has no support for them, so UseRaskSqlite(o => o.StrictTables = true) swaps in a
// migrations SQL generator that appends the keyword. These tests drive real files.
public sealed class RaskSqliteStrictTableTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"rask-sqlite-strict-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Strict_is_off_by_default()
    {
        await using (var context = NewContext(strict: false))
        {
            await context.Database.EnsureCreatedAsync();
        }

        Assert.DoesNotContain("STRICT", ReadDdl(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabling_it_creates_strict_tables()
    {
        await using (var context = NewContext(strict: true))
        {
            await context.Database.EnsureCreatedAsync();
        }

        var ddl = ReadDdl();
        Assert.EndsWith("STRICT", ddl.TrimEnd(), StringComparison.Ordinal);
        // The keyword belongs after the closing paren; anywhere else and SQLite would have refused it.
        Assert.Contains(") STRICT", ddl, StringComparison.Ordinal);
    }

    // The point of the feature: without STRICT, SQLite stores the text "lots" in an INTEGER column and
    // the wrong type flows back into the app later. With it, the write is refused at the source.
    [Fact]
    public async Task A_strict_table_refuses_a_value_of_the_wrong_type()
    {
        await using (var context = NewContext(strict: true))
        {
            await context.Database.EnsureCreatedAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO Rows (Quantity, Amount, Name) VALUES ('lots', '1.00', 'x')";

        var ex = await Assert.ThrowsAsync<SqliteException>(() => insert.ExecuteNonQueryAsync());
        Assert.Contains("cannot store TEXT value in INTEGER column", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_same_write_is_silently_accepted_without_strict()
    {
        await using (var context = NewContext(strict: false))
        {
            await context.Database.EnsureCreatedAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO Rows (Quantity, Amount, Name) VALUES ('lots', '1.00', 'x')";
            await insert.ExecuteNonQueryAsync();
        }

        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT typeof(Quantity) FROM Rows";
        Assert.Equal("text", await read.ExecuteScalarAsync());
    }

    // A decimal is TEXT in SQLite, so it is legal in a STRICT table — and still ordered by the
    // invariant collation. Guards the two features against each other.
    [Fact]
    public async Task A_decimal_still_works_in_a_strict_table()
    {
        await using (var context = NewContext(strict: true))
        {
            await context.Database.EnsureCreatedAsync();
            context.Rows.AddRange(
                new ProbeRow { Amount = 19.95m, Quantity = 1, Name = "a" },
                new ProbeRow { Amount = 2.00m, Quantity = 2, Name = "b" },
                new ProbeRow { Amount = 100.50m, Quantity = 3, Name = "c" },
                new ProbeRow { Amount = 9.50m, Quantity = 4, Name = "d" });
            await context.SaveChangesAsync();
        }

        await using var reader = NewContext(strict: true);
        var ordered = await reader.Rows.OrderBy(r => r.Amount).Select(r => r.Amount).ToListAsync();

        Assert.Equal([2.00m, 9.50m, 19.95m, 100.50m], ordered);
    }

    // SQLite's error for a rejected type names only the type. Ours names the table and column it came
    // from, and what to do about it — the difference between a five-minute and a fifty-minute debug.
    [Fact]
    public async Task A_column_type_strict_cannot_hold_is_reported_against_its_table_and_column()
    {
        await using var context = new CustomTypeDbContext(
            new DbContextOptionsBuilder<CustomTypeDbContext>()
                .UseRaskSqlite($"Data Source={_dbPath}", o => o.StrictTables = true)
                .Options);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Database.EnsureCreatedAsync());

        Assert.Contains("Widgets", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Label", ex.Message, StringComparison.Ordinal);
        Assert.Contains("VARCHAR(50)", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ANY", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("INT", true)]
    [InlineData("INTEGER", true)]
    [InlineData("integer", true)]
    [InlineData("REAL", true)]
    [InlineData("TEXT", true)]
    [InlineData("BLOB", true)]
    [InlineData("ANY", true)]
    [InlineData("VARCHAR(50)", false)]
    [InlineData("NUMERIC", false)]
    [InlineData("DECIMAL(9,2)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_allowed_type_set_matches_sqlites(string? columnType, bool allowed) =>
        Assert.Equal(allowed, SqliteStrictTypes.IsAllowed(columnType));

    private ProbeDbContext NewContext(bool strict) =>
        new(new DbContextOptionsBuilder<ProbeDbContext>()
            .UseRaskSqlite($"Data Source={_dbPath}", o => o.StrictTables = strict)
            .Options);

    private string ReadDdl()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'Rows'";
        return (string)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }

        public int Quantity { get; set; }

        public decimal Amount { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CustomTypeDbContext(DbContextOptions<CustomTypeDbContext> options) : DbContext(options)
    {
        public DbSet<Widget> Widgets => Set<Widget>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Widget>().Property(w => w.Label).HasColumnType("VARCHAR(50)");
    }

    private sealed class Widget
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }
}
