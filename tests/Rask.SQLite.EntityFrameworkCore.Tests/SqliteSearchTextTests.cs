using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.SQLite.EntityFrameworkCore.Tests;

/// <summary>An app's model on Rask's conventions, its order Hungarian, as TA's is.</summary>
public sealed class SearchContext(DbContextOptions<SearchContext> options) : DbContext(options)
{
    public DbSet<Driver> Drivers => Set<Driver>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ModelRegistry.ApplyConventions(configurationBuilder);
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
    }
}

// x.Loosely() on SQLite: case and accents aside, in no one language's order.
public sealed class SqliteSearchTextTests : IAsyncLifetime
{
    private readonly InMemorySqlite _database = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await using var context = Search();
        await context.Database.EnsureCreatedAsync(Cancellation);
        context.Drivers.AddRange(Named("Kovács Béla", "k-1"), Named("Csaba", "c-1"), Named("Árvíztűrő Tükörfúrógép", "a-1"), Named("Zsolt", "z-1"));
        await context.SaveChangesAsync(Cancellation);
    }

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Contains_finds_the_text_case_and_accents_aside()
    {
        await using var context = Search();
        var typed = "kovacs";

        var found = await context.Drivers.Where(d => d.Name.Loosely().Contains(typed)).Select(d => d.Code).ToListAsync(Cancellation);
        var inside = await context.Drivers.Where(d => d.Name.Loosely().Contains("TUKOR")).Select(d => d.Code).ToListAsync(Cancellation);

        Assert.Equal(["k-1"], found);
        Assert.Equal(["a-1"], inside);
    }

    [Fact]
    public async Task A_c_is_found_in_Csaba_though_the_app_s_Hungarian_order_counts_cs_as_one_letter()
    {
        await using var context = Search();
        var typed = "c";

        var loosely = await context.Drivers.Where(d => d.Name.Loosely().Contains(typed)).Select(d => d.Code).OrderBy(c => c).ToListAsync(Cancellation);

        Assert.Equal(["c-1", "k-1"], loosely);
    }

    [Fact]
    public async Task StartsWith_EndsWith_and_equality_ignore_case_and_accents_too()
    {
        await using var context = Search();
        var start = "arviz";
        var end = "FUROGEP";
        var whole = "KOVACS BELA";

        var starts = await context.Drivers.Where(d => d.Name.Loosely().StartsWith(start)).Select(d => d.Code).ToListAsync(Cancellation);
        var ends = await context.Drivers.Where(d => d.Name.Loosely().EndsWith(end)).Select(d => d.Code).ToListAsync(Cancellation);
        var equal = await context.Drivers.Where(d => d.Name.Loosely() == whole).Select(d => d.Code).ToListAsync(Cancellation);
        var notAtTheStart = await context.Drivers.CountAsync(d => d.Name.Loosely().StartsWith(end), Cancellation);

        Assert.Equal(["a-1"], starts);
        Assert.Equal(["a-1"], ends);
        Assert.Equal(["k-1"], equal);
        Assert.Equal(0, notAtTheStart);
    }

    [Fact]
    public void Only_the_query_names_the_collation_never_the_schema()
    {
        using var context = Search();

        var script = context.Database.GenerateCreateScript();
        var sql = context.Drivers.Where(d => d.Name.Loosely() == "x").ToQueryString();

        Assert.DoesNotContain("RASK_SEARCH", script, StringComparison.Ordinal);
        Assert.Contains("COLLATE \"RASK_SEARCH\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_UseSqlite_refuses_the_query_naming_the_options_that_have_it()
    {
        await using var context = new SearchContext(new DbContextOptionsBuilder<SearchContext>().UseSqlite(_database.ConnectionString).Options);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Drivers.Where(d => d.Name.Loosely().Contains("a")).ToListAsync(Cancellation));

        Assert.Contains("UseRaskSqlite", refused.Message, StringComparison.Ordinal);
    }

    private static Driver Named(string name, string code) => new() { Name = name, Code = code, Plate = code };

    private SearchContext Search() =>
        new(new DbContextOptionsBuilder<SearchContext>().UseRaskSqliteAt(_database.ConnectionString).Options);
}
