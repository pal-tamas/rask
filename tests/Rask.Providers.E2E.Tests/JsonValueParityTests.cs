using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Providers.E2E.Tests;

public sealed record WellLink(string FileId, int DbId);

/// <summary>A value object that holds a list and a secret, kept whole in one JSON column.</summary>
public sealed record WellDocument
{
    private WellDocument() { }

    public WellDocument(string provider, Secret<string>? key, ValueList<WellLink> links) => (Provider, Key, Links) = (provider, key, links);

    public string Provider { get; private init; } = "";

    public Secret<string>? Key { get; private init; }

    public ValueList<WellLink> Links { get; private init; } = [];
}

public sealed class WellHolder : Aggregate<Guid>
{
    private WellHolder() { }

    public string Name { get; private set; } = "";

    public ValueList<string> Codes { get; private set; } = [];

    public Secret<string>? Token { get; private set; }

    public WellDocument? Wells { get; private set; }

    public static WellHolder Called(string name, WellDocument wells, params string[] codes) =>
        new() { Id = Guid.NewGuid(), Name = name, Wells = wells, Codes = [.. codes], Token = Secret.Of("t-" + name) };

    public static void Configure(EntityTypeBuilder<WellHolder> b) => b.Json(h => h.Wells, column: "LocalSettings");
}

public sealed class WellHolderDbContext(DbContextOptions<WellHolderDbContext> options) : DbContext(options)
{
    public const string Schema = "json_values";

    public DbSet<WellHolder> Holders => Set<WellHolder>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Its own schema, because these tests share one PostgreSQL database with every other class here.
        modelBuilder.Entity<WellHolder>(b =>
        {
            b.ToTable("WellHolder", Database.IsSqlite() ? null : Schema);
            WellHolder.Configure(b);
        });
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>
///     <c>b.Json(…)</c>, <c>ValueList&lt;T&gt;</c> and <c>Secret&lt;T&gt;</c>: a secret inside the JSON survives as its
///     ciphertext. The per-provider translation (<c>json_extract</c>, <c>JSON_VALUE</c>, <c>jsonb</c>) is EF Core's, not SQL
///     Rask writes, so the round trip runs on SQLite alone; the container column's type is held on every server by
///     <see cref="JsonColumnParityTests" />.
/// </summary>
public abstract class JsonValueParityTests(ProviderLeg leg) : ParityTests<WellHolderDbContext>(leg)
{
    [Fact]
    public async Task A_json_value_object_with_its_list_and_secret_comes_back_as_it_was_saved()
    {
        RequireServer();
        await SeedAsync();

        await using var db = Open();
        var loaded = await db.Holders.SingleAsync(h => h.Name == "derby", Ct);

        Assert.Equal("ta", loaded.Wells!.Provider);
        Assert.Equal("k-derby", loaded.Wells.Key!.Reveal());
        Assert.Equal("t-derby", loaded.Token!.Reveal());
        Assert.Equal([("7", 1), ("8", 2)], loaded.Wells.Links.Select(l => (l.FileId, l.DbId)));
        Assert.Equal(ValueList.Of(["A1", "B2"]), loaded.Codes);
    }

    [Fact]
    public async Task A_member_of_the_json_value_object_and_a_value_list_are_searched_in_the_database()
    {
        RequireServer();
        await SeedAsync();

        await using var db = Open();
        var byProvider = await db.Holders.Where(h => h.Wells!.Provider == "mol").Select(h => h.Name).SingleAsync(Ct);
        var byLink = await db.Holders.Where(h => h.Wells!.Links.Any(l => l.DbId == 2)).Select(h => h.Name).SingleAsync(Ct);
        var byCode = await db.Holders.Where(h => h.Codes.Contains("C3")).Select(h => h.Name).SingleAsync(Ct);

        Assert.Equal("other", byProvider);
        Assert.Equal("derby", byLink);
        Assert.Equal("other", byCode);
    }

    private protected override WellHolderDbContext Open()
    {
        SecretKeeping.Default ??= new MarkedSecrets();
        return Leg.Open<WellHolderDbContext>(o => new WellHolderDbContext(o));
    }

    private async Task SeedAsync()
    {
        var derby = WellHolder.Called("derby", new WellDocument("ta", Secret.Of("k-derby"), [new("7", 1), new("8", 2)]), "A1", "B2");
        var other = WellHolder.Called("other", new WellDocument("mol", null, []), "C3");

        await using var db = Open();
        db.AddRange(derby, other);
        await db.SaveChangesAsync(Ct);
    }

    // A ciphertext a test can see is not the plaintext, without a key ring.
    private sealed class MarkedSecrets : ISecrets
    {
        public string Protect(string plaintext) => "enc:" + new string(plaintext.Reverse().ToArray());

        public string Unprotect(string stored) => new(stored["enc:".Length..].Reverse().ToArray());
    }
}

public sealed class SqliteJsonValueTests() : JsonValueParityTests(ProviderLeg.Sqlite("json-values"));

/// <summary>SQL Server, proved without a server: the JSON value object maps to one column and its filters translate.</summary>
public sealed class SqlServerJsonValueTranslationTests
{
    private const string Offline = "Server=(local);Database=rask_translation_only;Trusted_Connection=True";

    [Fact]
    public void The_json_value_object_is_one_column_and_its_members_translate()
    {
        using var db = new WellHolderDbContext(new DbContextOptionsBuilder<WellHolderDbContext>().UseRaskSqlServerAt(Offline).Options);

        var wells = db.Model.FindEntityType(typeof(WellHolder))!.FindComplexProperty(nameof(WellHolder.Wells))!;

        Assert.True(wells.ComplexType.IsMappedToJson());
        Assert.Equal("LocalSettings", wells.ComplexType.GetContainerColumnName());
        Assert.Contains("LocalSettings", db.Holders.Where(h => h.Wells!.Provider == "mol").ToQueryString(), StringComparison.Ordinal);
    }
}
