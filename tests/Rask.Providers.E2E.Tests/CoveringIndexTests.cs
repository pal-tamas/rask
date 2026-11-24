using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A payment, read by its payee with its amount — what a covering index is for.</summary>
public sealed class Remittance
{
    public Guid Id { get; set; }

    public Guid PayeeId { get; set; }

    public decimal Amount { get; set; }

    public string Note { get; set; } = "";
}

/// <summary>The payee's index carrying the amount and the note, under Rask's conventions.</summary>
public sealed class CoveredDbContext(DbContextOptions<CoveredDbContext> options) : DbContext(options)
{
    public const string Schema = "covering";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Remittance>(b =>
        {
            b.ToTable("Remittance", Database.IsSqlite() ? null : Schema);
            b.Property(r => r.Note).HasMaxLength(200);
            b.Property(r => r.Amount).HasPrecision(18, 2);
            b.HasIndex(r => r.PayeeId).Covering(r => new { r.Amount, r.Note });
        });
}

/// <summary><c>Covering(…)</c> on SQLite, which has no included columns: the index is its key column alone.</summary>
public sealed class SqliteCoveringIndexTests
{
    [Fact]
    public async Task The_index_is_created_over_its_key_column_alone()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var db = new CoveredDbContext(new DbContextOptionsBuilder<CoveredDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var definitions = await db.Database
            .SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type = 'index' AND name = 'IX_Remittance_PayeeId'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("CREATE INDEX \"IX_Remittance_PayeeId\" ON \"Remittance\" (\"PayeeId\")", Assert.Single(definitions));
    }
}

/// <summary><c>Covering(…)</c> on PostgreSQL: the index includes the covered columns.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresCoveringIndexTests
{
    [Fact]
    public async Task The_index_includes_the_covered_columns_beside_its_key()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var db = new CoveredDbContext(new DbContextOptionsBuilder<CoveredDbContext>()
            .UseRaskPostgresAt(Postgres.Required).Options);
        await Postgres.ResetSchemaAsync(db, CoveredDbContext.Schema);

        var definition = await db.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = {0} AND indexname = 'IX_Remittance_PayeeId'",
                CoveredDbContext.Schema)
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.EndsWith("USING btree (\"PayeeId\") INCLUDE (\"Amount\", \"Note\")", definition, StringComparison.Ordinal);
        await Postgres.DropSchemaAsync(db, CoveredDbContext.Schema);
    }
}

/// <summary><c>Covering(…)</c> on SQL Server: the index includes the covered columns, in a database of its own.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerCoveringIndexTests
{
    [Fact]
    public async Task The_index_includes_the_covered_columns_beside_its_key()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var db = new CoveredDbContext(new DbContextOptionsBuilder<CoveredDbContext>()
            .UseRaskSqlServerAt(SqlServer.Database("rask_covering")).Options);
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var columns = await db.Database
            .SqlQueryRaw<string>(
                """
                SELECT CONCAT(c.name, ':', ic.is_included_column) AS Value
                FROM sys.index_columns ic
                JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE ic.object_id = OBJECT_ID('covering.Remittance') AND i.name = 'IX_Remittance_PayeeId'
                ORDER BY ic.key_ordinal DESC, c.name
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["PayeeId:0", "Amount:1", "Note:1"], columns);
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }
}
