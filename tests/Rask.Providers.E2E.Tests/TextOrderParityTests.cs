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
}

/// <summary>Where the database compares text as the collation says — SQL Server, and SQLite through <c>UseRaskSqlite</c>.</summary>
public abstract class CaseInsensitiveTextOrderParityTests : TextOrderParityTests
{
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
}
