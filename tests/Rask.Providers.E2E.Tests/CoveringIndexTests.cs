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

/// <summary>A start and an end of a trip's odometer, stored as two columns of their own.</summary>
public sealed record HaulageKm(int Start, int End);

/// <summary>A haulage, read by its lane with the kilometres it covered.</summary>
public sealed class Haulage
{
    public Guid Id { get; set; }

    public string Lane { get; set; } = "";

    public HaulageKm Km { get; set; } = new(0, 0);
}

/// <summary>The lane's index carrying the haulage's kilometres: a value object of two members, named as the table always named them.</summary>
public sealed class CoveredHaulageContext(DbContextOptions<CoveredHaulageContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Haulage>(b =>
        {
            b.ToTable("Haulage");
            b.ComplexProperty(t => t.Km, km =>
            {
                km.Property(k => k.Start).HasColumnName("StartKm");
                km.Property(k => k.End).HasColumnName("EndKm");
            });
            b.HasIndex(t => t.Lane).Covering(t => t.Km);
        });
}

/// <summary>The lane's index carrying one member of the haulage's kilometres.</summary>
public sealed class CoveredHaulageEndContext(DbContextOptions<CoveredHaulageEndContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Haulage>(b =>
        {
            b.ToTable("Haulage");
            b.ComplexProperty(t => t.Km, km => km.Property(k => k.End).HasColumnName("EndKm"));
            b.HasIndex(t => t.Lane).Covering(t => new { t.Km.End });
        });
}

/// <summary><c>Covering(x =&gt; x.Km)</c> over a value object of several members: the index includes each of its columns.</summary>
public sealed class ComplexCoveringIndexScriptTests
{
    [Fact]
    public void On_SQL_Server_the_index_includes_each_column_of_the_value_object()
    {
        using var db = new CoveredHaulageContext(new DbContextOptionsBuilder<CoveredHaulageContext>()
            .UseSqlServer("Server=.;Database=rask_script_only").Options);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("ON [Haulage] ([Lane]) INCLUDE ([EndKm], [StartKm]);", script, StringComparison.Ordinal);
    }

    [Fact]
    public void On_PostgreSQL_the_index_includes_each_column_of_the_value_object()
    {
        using var db = new CoveredHaulageContext(new DbContextOptionsBuilder<CoveredHaulageContext>()
            .UseNpgsql("Host=localhost;Database=rask_script_only").Options);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("ON \"Haulage\" (\"Lane\") INCLUDE (\"EndKm\", \"StartKm\");", script, StringComparison.Ordinal);
    }

    [Fact]
    public void One_member_of_the_value_object_is_covered_by_its_path()
    {
        using var db = new CoveredHaulageEndContext(new DbContextOptionsBuilder<CoveredHaulageEndContext>()
            .UseSqlServer("Server=.;Database=rask_script_only").Options);

        var script = db.Database.GenerateCreateScript();

        Assert.Contains("ON [Haulage] ([Lane]) INCLUDE ([EndKm]);", script, StringComparison.Ordinal);
    }
}
