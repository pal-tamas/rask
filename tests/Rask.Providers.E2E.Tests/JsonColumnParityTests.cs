using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

public sealed record Enclosure(string Name, int Size);

/// <summary>A letter whose enclosures and labels are each kept as JSON in one column.</summary>
public sealed class Letter
{
    public Guid Id { get; set; }

    public string Subject { get; set; } = "";

    public List<Enclosure> Enclosures { get; set; } = [];

    public List<string> Labels { get; set; } = [];
}

/// <summary>JSON columns with no provider's type named: Rask's conventions pick each database's own.</summary>
public sealed class LetterDbContext(DbContextOptions<LetterDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_json_columns";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsNpgsql() ? Schema : null);
        modelBuilder.Entity<Letter>().ComplexCollection(l => l.Enclosures, enclosures => enclosures.ToJson());
    }
}

/// <summary>
///     A JSON column the model names no type for is created, written and read back on every provider — so no app has to
///     pin <c>nvarchar(max)</c>, which SQLite cannot parse, to keep SQL Server's away from its <c>json</c> type.
/// </summary>
public abstract class JsonColumnParityTests(ProviderLeg leg) : ParityTests<LetterDbContext>(leg)
{
    [Fact]
    public async Task A_json_column_with_no_type_named_round_trips()
    {
        RequireServer();
        await using (var write = Open())
        {
            write.Add(new Letter { Id = Guid.NewGuid(), Subject = "tax", Enclosures = [new("invoice.pdf", 120), new("receipt.pdf", 8)], Labels = ["paid"] });
            await write.SaveChangesAsync(Ct);
        }

        await using var read = Open();
        var letter = await read.Set<Letter>().SingleAsync(Ct);

        Assert.Equal([new Enclosure("invoice.pdf", 120), new Enclosure("receipt.pdf", 8)], letter.Enclosures);
        Assert.Equal(["paid"], letter.Labels);
    }

    [Fact]
    public async Task A_query_reaches_into_the_json_column()
    {
        RequireServer();
        await using (var write = Open())
        {
            write.Add(new Letter { Id = Guid.NewGuid(), Subject = "tax", Enclosures = [new("invoice.pdf", 120)], Labels = ["paid"] });
            write.Add(new Letter { Id = Guid.NewGuid(), Subject = "note", Enclosures = [], Labels = [] });
            await write.SaveChangesAsync(Ct);
        }

        await using var read = Open();
        var subjects = await read.Set<Letter>().Where(l => l.Enclosures.Any(e => e.Size > 100) && l.Labels.Contains("paid"))
            .Select(l => l.Subject).ToListAsync(Ct);

        Assert.Equal(["tax"], subjects);
    }

    private protected override LetterDbContext Open() => Leg.Open<LetterDbContext>(o => new LetterDbContext(o));
}

public sealed class SqliteJsonColumnParityTests() : JsonColumnParityTests(ProviderLeg.Sqlite("json-columns"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerJsonColumnParityTests() : JsonColumnParityTests(ProviderLeg.SqlServer("rask_e2e_json_columns"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresJsonColumnParityTests() : JsonColumnParityTests(ProviderLeg.Postgres(LetterDbContext.Schema));
