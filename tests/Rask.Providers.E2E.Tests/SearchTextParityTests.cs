using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

public sealed class Searched
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";
}

/// <summary>
///     An app's model on Rask's conventions whose own order is Hungarian, as TA's is — the database created under that
///     collation on SQL Server — searched beside it with <c>x.Loosely()</c>.
/// </summary>
public sealed class SearchTextDbContext(DbContextOptions<SearchTextDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_search_text";

    public DbSet<Searched> Rows => Set<Searched>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ModelRegistry.ApplyConventions(configurationBuilder);
        configurationBuilder.OrderTextBy(TextOrderDbContext.Collation);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (Database.IsSqlServer())
        {
            modelBuilder.UseCollation(TextOrderDbContext.Collation);
        }
        else if (Database.IsNpgsql())
        {
            modelBuilder.HasDefaultSchema(Schema);
        }
    }
}

/// <summary><c>x.Loosely()</c> on each provider, written once: case and accents aside, the app's own order apart.</summary>
public abstract class SearchTextParityTests(ProviderLeg leg) : ParityTests<SearchTextDbContext>(leg)
{
    [Fact]
    public async Task Contains_finds_the_text_case_and_accents_aside()
    {
        RequireServer();
        await using var db = await SeededAsync();
        var typed = "kovacs";

        var found = await db.Rows.Where(r => r.Name.Loosely().Contains(typed)).Select(r => r.Code).ToListAsync(Ct);
        var inside = await db.Rows.Where(r => r.Name.Loosely().Contains("TUKOR")).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(["k-1"], found);
        Assert.Equal(["a-1"], inside);
    }

    [Fact]
    public async Task A_c_is_found_in_Csaba_where_the_app_s_Hungarian_order_counts_cs_as_one_letter()
    {
        RequireServer();
        await using var db = await SeededAsync();
        var typed = "c";

        var found = await db.Rows.Where(r => r.Name.Loosely().Contains(typed)).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(["c-1", "k-1"], found.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task StartsWith_EndsWith_and_equality_ignore_case_and_accents_too()
    {
        RequireServer();
        await using var db = await SeededAsync();
        var start = "arviz";
        var end = "FUROGEP";
        var whole = "KOVACS BELA";

        var starts = await db.Rows.Where(r => r.Name.Loosely().StartsWith(start)).Select(r => r.Code).ToListAsync(Ct);
        var ends = await db.Rows.Where(r => r.Name.Loosely().EndsWith(end)).Select(r => r.Code).ToListAsync(Ct);
        var equal = await db.Rows.Where(r => r.Name.Loosely() == whole).Select(r => r.Code).ToListAsync(Ct);

        Assert.Equal(["a-1"], starts);
        Assert.Equal(["a-1"], ends);
        Assert.Equal(["k-1"], equal);
    }

    private protected override SearchTextDbContext Open() => Leg.Open<SearchTextDbContext>(o => new SearchTextDbContext(o));

    private async Task<SearchTextDbContext> SeededAsync()
    {
        var db = Open();
        db.Rows.AddRange(
            new Searched { Name = "Kovács Béla", Code = "k-1" },
            new Searched { Name = "Csaba", Code = "c-1" },
            new Searched { Name = "Árvíztűrő Tükörfúrógép", Code = "a-1" },
            new Searched { Name = "Zsolt", Code = "z-1" });
        await db.SaveChangesAsync(Ct);
        return db;
    }
}

public sealed class SqliteSearchTextParityTests() : SearchTextParityTests(ProviderLeg.Sqlite("search-text"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerSearchTextParityTests() : SearchTextParityTests(ProviderLeg.SqlServer("rask_e2e_search_text"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresSearchTextParityTests() : SearchTextParityTests(ProviderLeg.Postgres(SearchTextDbContext.Schema));
