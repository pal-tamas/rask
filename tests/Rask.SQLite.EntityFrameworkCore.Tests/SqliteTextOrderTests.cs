using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

public sealed class Driver
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    public string Plate { get; set; } = "";
}

/// <summary>A model that names how text compares, as an app whose production database is SQL Server does.</summary>
public sealed class HungarianContext(DbContextOptions<HungarianContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Driver>(b =>
        {
            b.HasIndex(d => d.Code).IsUnique();
            b.Property(d => d.Plate).UseCollation("NOCASE");
        });
}

/// <summary>A name that may be missing, to order with the rest.</summary>
public sealed class Racer
{
    public int Id { get; set; }

    public string? Name { get; set; }
}

/// <summary>A Hungarian model of racers, some of them nameless.</summary>
public sealed class HungarianRacersContext(DbContextOptions<HungarianRacersContext> options) : DbContext(options)
{
    public DbSet<Racer> Racers => Set<Racer>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
}

/// <summary>The same table with no text order named: SQLite's own comparison, untouched.</summary>
public sealed class OrdinalContext(DbContextOptions<OrdinalContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();
}

// OrderTextBy("Hungarian_100_CI_AS") on SQLite: the text compares as SQL Server's collation of that name does.
public sealed class SqliteTextOrderTests : IAsyncLifetime
{
    private readonly InMemorySqlite _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var context = Hungarian();
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Drivers.AddRange(
            Named("Zsolt", "z-1"),
            Named("Csaba", "c-1"),
            Named("Béla", "b-1"),
            Named("Cecil", "c-2"),
            Named("Ödön", "o-2"),
            Named("Ottó", "o-1"),
            Named("Árvíztűrő Tükörfúrógép", "a-1"),
            Named("50%_off", "p-1"),
            Named("500 off", "p-2"));
        await context.SaveChangesAsync(Cancellation);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public void Every_text_column_is_created_with_the_text_collation()
    {
        using var context = Hungarian();

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Name\" TEXT COLLATE \"RASK_TEXT\" NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Code\" TEXT COLLATE \"RASK_TEXT\" NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_with_a_collation_of_its_own_keeps_it()
    {
        using var context = Hungarian();

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Plate\" TEXT COLLATE \"NOCASE\" NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Equality_ignores_case_beyond_ASCII()
    {
        await using var context = Hungarian();

        var found = await context.Drivers.Where(d => d.Name == "árvíztűrő tükörfúrógép").Select(d => d.Code)
            .ToListAsync(Cancellation);

        Assert.Equal(["a-1"], found);
    }

    [Fact]
    public async Task Equality_keeps_accented_letters_apart()
    {
        await using var context = Hungarian();

        var found = await context.Drivers.CountAsync(d => d.Name == "Arvizturo Tukorfurogep", Cancellation);

        Assert.Equal(0, found);
    }

    [Fact]
    public async Task A_unique_index_refuses_the_same_text_in_another_case()
    {
        await using var context = Hungarian();

        context.Drivers.Add(Named("Csaba Again", "C-1"));
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Cancellation));

        Assert.Contains("UNIQUE", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_in_the_app_s_order_is_in_Hungarian_order()
    {
        await using var context = Hungarian();

        var ordered = await context.Drivers.Where(d => d.Code.StartsWith("o") || d.Code == "c-1" || d.Code == "c-2" || d.Code == "b-1")
            .OrderBy(d => d.Name.InTextOrder()).Select(d => d.Name).ToListAsync(Cancellation);

        Assert.Equal(["Béla", "Cecil", "Csaba", "Ottó", "Ödön"], ordered);
    }

    [Fact]
    public void Text_in_the_app_s_order_is_written_with_the_text_collation()
    {
        using var context = Hungarian();

        var sql = context.Drivers.OrderBy(d => d.Name.InTextOrder()).ToQueryString();

        Assert.Contains("ORDER BY \"d\".\"Name\" COLLATE \"RASK_TEXT\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contains_ignores_case_beyond_ASCII()
    {
        await using var context = Hungarian();
        var part = "TÜKÖR";

        var constant = await context.Drivers.Where(d => d.Name.Contains("árv")).Select(d => d.Code).ToListAsync(Cancellation);
        var parameter = await context.Drivers.Where(d => d.Name.Contains(part)).Select(d => d.Code).ToListAsync(Cancellation);

        Assert.Equal(["a-1"], constant);
        Assert.Equal(["a-1"], parameter);
    }

    [Fact]
    public async Task StartsWith_and_EndsWith_ignore_case_beyond_ASCII()
    {
        await using var context = Hungarian();
        var end = "FÚRÓGÉP";

        var starts = await context.Drivers.Where(d => d.Name.StartsWith("ÁRVÍZ")).Select(d => d.Code).ToListAsync(Cancellation);
        var ends = await context.Drivers.Where(d => d.Name.EndsWith(end)).Select(d => d.Code).ToListAsync(Cancellation);

        Assert.Equal(["a-1"], starts);
        Assert.Equal(["a-1"], ends);
    }

    [Fact]
    public async Task A_wildcard_in_the_searched_text_is_matched_as_written()
    {
        await using var context = Hungarian();
        var start = "50%_";

        var found = await context.Drivers.Where(d => d.Name.StartsWith(start)).Select(d => d.Code).ToListAsync(Cancellation);

        Assert.Equal(["p-1"], found);
    }

    [Fact]
    public async Task Like_compares_by_the_collation_with_its_wildcards()
    {
        await using var context = Hungarian();

        var found = await context.Drivers.Where(d => EF.Functions.Like(d.Name, "%TŰRŐ_T%")).Select(d => d.Code)
            .ToListAsync(Cancellation);

        Assert.Equal(["a-1"], found);
    }

    [Fact]
    public async Task IndexOf_counts_characters_as_SQLite_does()
    {
        await using var context = Hungarian();

        var at = await context.Drivers.Where(d => d.Code == "a-1").Select(d => d.Name.IndexOf("TÜKÖR")).SingleAsync(Cancellation);

        Assert.Equal(10, at);
    }

    [Fact]
    public async Task A_strict_table_takes_the_text_collation()
    {
        await using var database = new InMemorySqlite();
        await using var context = new HungarianContext(new DbContextOptionsBuilder<HungarianContext>()
            .UseRaskSqliteAt(database.ConnectionString, o => o.StrictTables = true).Options);
        await context.Database.EnsureCreatedAsync(Cancellation);

        context.Drivers.Add(Named("Csaba", "c-1"));
        await context.SaveChangesAsync(Cancellation);
        var found = await context.Drivers.CountAsync(d => d.Name == "CSABA", Cancellation);

        Assert.Equal(1, found);
    }

    [Fact]
    public async Task A_model_that_names_no_text_order_compares_as_SQLite_does()
    {
        await using var database = new InMemorySqlite();
        await using var context = new OrdinalContext(new DbContextOptionsBuilder<OrdinalContext>()
            .UseRaskSqliteAt(database.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Drivers.Add(Named("Árvíztűrő Csaba", "c-1"));
        await context.SaveChangesAsync(Cancellation);

        var equal = await context.Drivers.CountAsync(d => d.Name == "árvíztűrő csaba", Cancellation);
        var contains = await context.Drivers.CountAsync(d => d.Name.Contains("árv"), Cancellation);

        Assert.Equal(0, equal);
        Assert.Equal(0, contains);
    }

    [Fact]
    public async Task The_whole_Hungarian_alphabet_orders_in_the_database_as_it_does_in_memory()
    {
        await using var keeper = await Kept();
        await using var context = await Racers(keeper, "Úr", "Csaba", "Ödön", "Cukor", "Uzsok", "Ottó", "Ügy", "Őz", "Zsolt", "Zoltán");

        var ordered = await context.Racers.OrderBy(r => r.Name!.InTextOrder()).Select(r => r.Name).ToListAsync(Cancellation);

        Assert.Equal(["Cukor", "Csaba", "Ottó", "Ödön", "Őz", "Úr", "Uzsok", "Ügy", "Zoltán", "Zsolt"], ordered);
    }

    [Fact]
    public async Task Descending_text_order_is_the_ascending_order_reversed()
    {
        await using var keeper = await Kept();
        await using var context = await Racers(keeper, "Úr", "Csaba", "Ödön", "Cukor", "Uzsok", "Ottó", "Ügy", "Őz", "Zsolt", "Zoltán");

        var ascending = await context.Racers.OrderBy(r => r.Name!.InTextOrder()).Select(r => r.Name).ToListAsync(Cancellation);
        var descending = await context.Racers.OrderByDescending(r => r.Name!.InTextOrder()).Select(r => r.Name).ToListAsync(Cancellation);

        Assert.Equal(Enumerable.Reverse(ascending), descending);
    }

    [Fact]
    public async Task A_missing_name_sorts_first_ascending_and_last_descending_as_on_SQL_Server()
    {
        await using var keeper = await Kept();
        await using var context = await Racers(keeper, "Csaba", null, "Cukor");

        var ascending = await context.Racers.OrderBy(r => r.Name!.InTextOrder()).Select(r => r.Name).ToListAsync(Cancellation);
        var descending = await context.Racers.OrderByDescending(r => r.Name!.InTextOrder()).Select(r => r.Name).ToListAsync(Cancellation);

        Assert.Equal([null, "Cukor", "Csaba"], ascending);
        Assert.Equal(["Csaba", "Cukor", null], descending);
    }

    private static async Task<SqliteConnection> Kept()
    {
        var keeper = new SqliteConnection($"Data Source=rask-sqlite-racers-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        await keeper.OpenAsync(Cancellation);
        return keeper;
    }

    private static async Task<HungarianRacersContext> Racers(SqliteConnection keeper, params string?[] names)
    {
        var context = new HungarianRacersContext(new DbContextOptionsBuilder<HungarianRacersContext>()
            .UseRaskSqliteAt(keeper.ConnectionString).Options);
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Racers.AddRange(names.Select(static name => new Racer { Name = name }));
        await context.SaveChangesAsync(Cancellation);
        context.ChangeTracker.Clear();
        return context;
    }

    private static Driver Named(string name, string code) => new() { Name = name, Code = code, Plate = code };

    private HungarianContext Hungarian() =>
        new(new DbContextOptionsBuilder<HungarianContext>().UseRaskSqliteAt(_database.ConnectionString).Options);
}
