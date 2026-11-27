using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

public sealed class Speaker
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";
}

/// <summary>
///     A model that names how text compares by its SQL Server collation, as TA's and Eszaf's do: on SQL Server the database
///     itself is created under that collation, as theirs are.
/// </summary>
public sealed class TextOrderDbContext(DbContextOptions<TextOrderDbContext> options) : DbContext(options)
{
    public const string Collation = "Hungarian_100_CI_AS";

    public const string Schema = "rask_text_order";

    public DbSet<Speaker> Speakers => Set<Speaker>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy(Collation);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (string.Equals(Database.ProviderName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
        {
            modelBuilder.UseCollation(Collation);
        }
        else if (string.Equals(Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            modelBuilder.HasDefaultSchema(Schema);
        }

        modelBuilder.Entity<Speaker>().HasIndex(s => s.Code).IsUnique();
    }
}

/// <summary>
///     <c>c.OrderTextBy("Hungarian_100_CI_AS")</c> on each provider, written once: the order a query asks for with
///     <c>InTextOrder()</c> is Hungarian everywhere.
/// </summary>
public abstract class TextOrderParityTests : IAsyncLifetime
{
    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private protected abstract bool Available { get; }

    private protected abstract string SkipReason { get; }

    private protected abstract TextOrderDbContext NewContext();

    private protected abstract Task Recreate(DbContext db);

    private protected abstract Task Drop(DbContext db);

    public async ValueTask InitializeAsync()
    {
        if (!Available)
        {
            return;
        }

        await using var db = NewContext();
        await Recreate(db);
        db.Speakers.AddRange(
            Named("Zsolt", "z-1"),
            Named("Csaba", "c-1"),
            Named("béla", "b-1"),
            Named("Cecil", "c-2"),
            Named("Ödön", "o-2"),
            Named("Ottó", "o-1"),
            Named("Árvíztűrő Tükörfúrógép", "a-1"));
        await db.SaveChangesAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Available)
        {
            await using var db = NewContext();
            await Drop(db);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Text_in_the_app_s_order_is_in_Hungarian_order()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();

        var ordered = await db.Speakers.Where(s => s.Code != "a-1" && s.Code != "z-1")
            .OrderBy(s => s.Name.InTextOrder()).Select(s => s.Name).ToListAsync(Ct);

        Assert.Equal(["béla", "Cecil", "Csaba", "Ottó", "Ödön"], ordered);
    }

    private protected static Speaker Named(string name, string code) => new() { Name = name, Code = code };

    /// <summary>A context on the seeded table with <paramref name="speakers" /> added beside the seed.</summary>
    private protected async Task<TextOrderDbContext> WithAsync(params Speaker[] speakers)
    {
        var db = NewContext();
        db.Speakers.AddRange(speakers);
        await db.SaveChangesAsync(Ct);
        return db;
    }
}

/// <summary>Where the database compares text as the collation says — SQL Server, and SQLite through <c>UseRaskSqlite</c>.</summary>
public abstract class CaseInsensitiveTextOrderParityTests : TextOrderParityTests
{
    /// <summary>
    ///     Whether <c>Replace</c> matches the value case aside: SQL Server's <c>REPLACE</c> does, by the collation;
    ///     SQLite's <c>replace</c> and PostgreSQL's (which refuses a nondeterministic collation) replace the exact value.
    /// </summary>
    private protected virtual bool ReplaceIgnoresCase => false;

    [Fact]
    public async Task Equality_ignores_case_beyond_ASCII_and_keeps_accents_apart()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();

        var found = await db.Speakers.Where(s => s.Name == "ÁRVÍZTŰRŐ TÜKÖRFÚRÓGÉP").Select(s => s.Code).ToListAsync(Ct);
        var unaccented = await db.Speakers.CountAsync(s => s.Name == "Arvizturo Tukorfurogep", Ct);

        Assert.Equal(["a-1"], found);
        Assert.Equal(0, unaccented);
    }

    [Fact]
    public async Task Searching_inside_the_text_ignores_case_beyond_ASCII()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var end = "FÚRÓGÉP";

        var contains = await db.Speakers.Where(s => s.Name.Contains("árv")).Select(s => s.Code).ToListAsync(Ct);
        var starts = await db.Speakers.Where(s => s.Name.StartsWith("ÁRVÍZ")).Select(s => s.Code).ToListAsync(Ct);
        var ends = await db.Speakers.Where(s => s.Name.EndsWith(end)).Select(s => s.Code).ToListAsync(Ct);

        Assert.Equal(["a-1"], contains);
        Assert.Equal(["a-1"], starts);
        Assert.Equal(["a-1"], ends);
    }

    [Fact]
    public async Task A_typed_search_ignores_case_and_keeps_accents_apart()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = await WithAsync(Named("Kovács Béla", "k-1"));
        var typed = "kovács";
        var start = "KOV";
        // Not "S BÉLA": in Hungarian "cs" is one letter, and SQL Server's Hungarian_100 collation does not split it, so
        // "Kovács Béla" does not end in "s Béla" there. A search for the whole letter is the same on every provider.
        var end = "ÁCS BÉLA";
        var unaccented = "kovacs";

        var contains = await db.Speakers.Where(s => s.Name.Contains(typed)).Select(s => s.Code).ToListAsync(Ct);
        var starts = await db.Speakers.Where(s => s.Name.StartsWith(start)).Select(s => s.Code).ToListAsync(Ct);
        var ends = await db.Speakers.Where(s => s.Name.EndsWith(end)).Select(s => s.Code).ToListAsync(Ct);
        var accentsApart = await db.Speakers.CountAsync(s => s.Name.Contains(unaccented), Ct);

        Assert.Equal(["k-1"], contains);
        Assert.Equal(["k-1"], starts);
        Assert.Equal(["k-1"], ends);
        Assert.Equal(0, accentsApart);
    }

    [Fact]
    public async Task A_percent_or_an_underscore_in_the_search_matches_only_itself()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = await WithAsync(Named("50% off_now", "p-1"), Named("500 offXnow", "p-2"));
        var percent = "0% OFF_";
        var underscore = "_";

        var found = await db.Speakers.Where(s => s.Name.Contains(percent)).Select(s => s.Code).ToListAsync(Ct);
        var underscored = await db.Speakers.Where(s => s.Name.Contains(underscore)).Select(s => s.Code).ToListAsync(Ct);

        Assert.Equal(["p-1"], found);
        Assert.Equal(["p-1"], underscored);
    }

    [Fact]
    public async Task Searching_by_another_column_ignores_case_and_takes_its_percent_literally()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = await WithAsync(
            Named("Nagy X-9 Elek", "x-9"),
            Named("Abxdz", "b%d"),
            Named("Ab%Ez", "B%E"),
            Named("X-7 Kiss", "x-7"),
            Named("Kiss x-8", "X-8"));

        var contains = await db.Speakers.Where(s => s.Name.Contains(s.Code)).Select(s => s.Code).ToListAsync(Ct);
        var starts = await db.Speakers.Where(s => s.Name.StartsWith(s.Code)).Select(s => s.Code).ToListAsync(Ct);
        var ends = await db.Speakers.Where(s => s.Name.EndsWith(s.Code)).Select(s => s.Code).ToListAsync(Ct);

        Assert.Equal(["B%E", "X-8", "x-7", "x-9"], contains.Order(StringComparer.Ordinal));
        Assert.Equal(["x-7"], starts);
        Assert.Equal(["X-8"], ends);
    }

    [Fact]
    public async Task IndexOf_ignores_case_and_keeps_accents_apart()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var typed = "TŰRŐ";
        var unaccented = "turo";

        var at = await db.Speakers.Where(s => s.Code == "a-1").Select(s => s.Name.IndexOf(typed)).SingleAsync(Ct);
        var missing = await db.Speakers.Where(s => s.Code == "a-1").Select(s => s.Name.IndexOf(unaccented)).SingleAsync(Ct);

        Assert.Equal(5, at);
        Assert.Equal(-1, missing);
    }

    [Fact]
    public async Task Replace_replaces_the_value_in_the_case_the_provider_matches_it()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var exact = "tűrő";
        var upper = "TŰRŐ";

        var replaced = await db.Speakers.Where(s => s.Code == "a-1").Select(s => s.Name.Replace(exact, "-")).SingleAsync(Ct);
        var otherCase = await db.Speakers.Where(s => s.Code == "a-1").Select(s => s.Name.Replace(upper, "-")).SingleAsync(Ct);

        Assert.Equal("Árvíz- Tükörfúrógép", replaced);
        Assert.Equal(ReplaceIgnoresCase ? "Árvíz- Tükörfúrógép" : "Árvíztűrő Tükörfúrógép", otherCase);
    }

    [Fact]
    public async Task A_unique_index_refuses_the_same_text_in_another_case()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();

        db.Speakers.Add(Named("Csaba Again", "C-1"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerTextOrderTests : CaseInsensitiveTextOrderParityTests
{
    private const string DatabaseName = "rask_e2e_text_order";

    private protected override bool Available => SqlServer.Available;

    private protected override bool ReplaceIgnoresCase => true;

    private protected override string SkipReason => SqlServer.SkipReason;

    private protected override TextOrderDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TextOrderDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);

    private protected override async Task Recreate(DbContext db)
    {
        await db.Database.EnsureDeletedAsync(Ct);
        await db.Database.EnsureCreatedAsync(Ct);
    }

    private protected override Task Drop(DbContext db) => db.Database.EnsureDeletedAsync();
}

public sealed class SqliteTextOrderTests : CaseInsensitiveTextOrderParityTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rask-e2e-text-order-{Guid.NewGuid():N}.db");

    private protected override bool Available => true;

    private protected override string SkipReason => "";

    private protected override TextOrderDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TextOrderDbContext>().UseRaskSqliteAt($"Data Source={_path}").Options);

    private protected override Task Recreate(DbContext db) => db.Database.EnsureCreatedAsync(Ct);

    private protected override Task Drop(DbContext db)
    {
        SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
        File.Delete(_path);
        return Task.CompletedTask;
    }
}

/// <summary>
///     PostgreSQL through <c>UseRaskPostgres</c>: a nondeterministic ICU collation its migrations create (PostgreSQL 18+,
///     for <c>LIKE</c>), so equality, a unique index and <c>Contains</c> compare as SQL Server's do.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresTextOrderTests : CaseInsensitiveTextOrderParityTests
{
    private protected override bool Available => Postgres.Available;

    private protected override string SkipReason => Postgres.SkipReason;

    private protected override TextOrderDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TextOrderDbContext>().UseRaskPostgresAt(Postgres.Required).Options);

    private protected override Task Recreate(DbContext db) => Postgres.ResetSchemaAsync(db, TextOrderDbContext.Schema);

    private protected override Task Drop(DbContext db) => Postgres.DropSchemaAsync(db, TextOrderDbContext.Schema);

    [Fact]
    public async Task A_regular_expression_and_ILike_run_on_the_nondeterministic_column()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();

        var regex = await db.Speakers.Where(s => Regex.IsMatch(s.Name, "^Árv")).Select(s => s.Code).ToListAsync(Ct);
        var ilike = await db.Speakers.Where(s => EF.Functions.ILike(s.Name, "árvíz%")).Select(s => s.Code).ToListAsync(Ct);

        Assert.Equal(["a-1"], regex);
        Assert.Equal(["a-1"], ilike);
    }

    [Fact]
    public async Task Replace_s_answer_compares_by_the_column_s_collation_again()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using var db = NewContext();
        var exact = "tűrő";

        var found = await db.Speakers.Where(s => s.Name.Replace(exact, "-") == "ÁRVÍZ- TÜKÖRFÚRÓGÉP").Select(s => s.Code).ToListAsync(Ct);

        Assert.Equal(["a-1"], found);
    }
}
