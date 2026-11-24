using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A row whose key comes from a sequence starting at 1000.</summary>
public sealed class SequencedRow
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

public sealed class SequenceDbContext(DbContextOptions<SequenceDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_sequences";

    public DbSet<SequencedRow> Rows => Set<SequencedRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<SequencedRow>(b => b.Property(r => r.Id).FromSequence("SequencedRows", start: 1000));
    }
}

/// <summary>
///     <c>FromSequence</c> on every provider: a key handed out as the row is added — before its insert — from the
///     provider's sequence (SQL Server, PostgreSQL) or Rask's own table (SQLite), and never the same twice.
/// </summary>
public abstract class SequenceParityTests(ProviderLeg leg) : ParityTests<SequenceDbContext>(leg)
{
    [Fact]
    public async Task An_added_row_has_its_key_before_the_insert_from_the_sequences_start()
    {
        RequireServer();
        await using var db = Open();

        var row = new SequencedRow { Name = "MOL" };
        db.Rows.Add(row);
        var before = row.Id;
        await db.SaveChangesAsync(Ct);

        Assert.Equal(1000, before);
        Assert.Equal("MOL", (await db.Rows.SingleAsync(r => r.Id == 1000, Ct)).Name);
    }

    [Fact]
    public async Task Contexts_of_their_own_never_take_the_same_key()
    {
        RequireServer();

        var keys = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using var db = Open();
            var row = new SequencedRow { Name = "row " + i };
            await db.Rows.AddAsync(row, Ct);
            await db.SaveChangesAsync(Ct);
            return row.Id;
        }));

        Assert.Equal(8, keys.Distinct().Count());
        Assert.All(keys, key => Assert.InRange(key, 1000, 1007));
    }

    private protected override SequenceDbContext Open() => Leg.Open<SequenceDbContext>(o => new SequenceDbContext(o));
}

public sealed class SqliteSequenceParityTests() : SequenceParityTests(ProviderLeg.Sqlite("sequences"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerSequenceParityTests() : SequenceParityTests(ProviderLeg.SqlServer("rask_e2e_sequences"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresSequenceParityTests() : SequenceParityTests(ProviderLeg.Postgres(SequenceDbContext.Schema));
