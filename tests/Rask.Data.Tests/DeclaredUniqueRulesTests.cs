using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>Only something to declare filtered indexes on: which of them can be asked as a query.</summary>
public sealed class Berth
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public int? TenantId { get; set; }

    public string Pier { get; set; } = "";

    public bool Active { get; set; }

    public string Label { get; set; } = "";
}

public sealed class BerthContext(DbContextOptions<BerthContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Berth>(b =>
        {
            b.HasIndex(x => new { x.Code, x.TenantId }).IsUnique("code").HasFilter("([TenantId] IS NOT NULL)");
            b.HasIndex(x => x.Pier).IsUnique("pier").HasFilter("[Active] = 1");
            b.HasIndex(x => x.Label).IsUnique();
        });
}

/// <summary>
/// On a schema Rask does not own, a declared unique rule is asked as a QUERY before the save, because the
/// index it describes may not exist in the database — and it refuses the save with the failure the index's
/// violation would have produced.
/// </summary>
/// <remarks>
/// Every test here runs against tables whose unique indexes have been DROPPED, so nothing but the check can
/// refuse a duplicate: a refusal with no inner exception is the check's, never the database's.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class DeclaredUniqueRulesTests : IAsyncLifetime
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-declared-rules-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<DomainContext>();
        services.AddDeclaredRuleChecks();
        services.AddDbContextFactory<DomainContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));

        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        // The tables, and then no unique index on any of them: the shape of a database somebody else made,
        // where what the model declares was never created.
        await using var context = await OpenAsync();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var indexes = await context.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Lane' AND sql IS NOT NULL")
            .ToListAsync(TestContext.Current.CancellationToken);
        foreach (var index in indexes)
        {
#pragma warning disable EF1002 // an identifier read back from the catalogue, not a value
            await context.Database.ExecuteSqlRawAsync($"DROP INDEX \"{index}\"", TestContext.Current.CancellationToken);
#pragma warning restore EF1002
        }
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_duplicate_is_refused_by_the_check_when_the_index_is_not_there()
    {
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Gyor", "Graz").With(name: "Budapest–Vienna")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Lane.NameTaken, failure.Message);
        Assert.Equal(["Name"], failure.Fields);
        Assert.Equal("IX_Lane_Name_TenantId", failure.Source);
        Assert.Null(refused.InnerException);
        Assert.Equal(1, await RowsAsync());
    }

    [Fact]
    public async Task Another_tenant_can_hold_the_same_value_because_the_check_asks_through_the_tenant_filter()
    {
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        await SaveAsync(Globex, Lane.Between("Budapest", "Vienna"));

        Assert.Equal(2, await RowsAsync());
    }

    [Fact]
    public async Task A_rule_over_several_columns_is_one_failure_over_all_of_them()
    {
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Budapest", "Vienna").With(name: "other", code: "OTHER", slug: "other")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(["Origin", "Target"], failure.Fields);
        Assert.Equal([Lane.RouteTaken], refused.Errors["Origin"]);
        Assert.Equal([Lane.RouteTaken], refused.Errors["Target"]);
    }

    [Fact]
    public async Task Every_rule_a_row_breaks_is_reported_in_one_refusal()
    {
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Budapest", "Vienna")));

        Assert.Equal(
            [Lane.RouteTaken, Lane.NameTaken, Lane.CodeTaken],
            refused.Failures.Select(f => f.Message).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_row_does_not_collide_with_itself_when_it_is_saved_again()
    {
        var id = await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        using (Tenant.Use(Acme))
        {
            await Lane.Update(id, l => l.With(name: "Budapest–Vienna", slug: "renamed"), cancellationToken: TestContext.Current.CancellationToken);
            await Lane.Update(id, l => l.With(name: "Pest–Vienna"), cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, await RowsAsync());
    }

    [Fact]
    public async Task A_rename_onto_a_value_another_row_holds_is_refused()
    {
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));
        var other = await SaveAsync(Acme, Lane.Between("Gyor", "Graz"));

        RaskValidationException refused;
        using (Tenant.Use(Acme))
        {
            refused = await Assert.ThrowsAsync<RaskValidationException>(() => Lane.Update(
                other, l => l.With(name: "Budapest–Vienna"), cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal(["Name"], Assert.Single(refused.Failures).Fields);
    }

    [Fact]
    public async Task Two_rows_of_one_save_that_break_a_rule_between_them_are_refused()
    {
        await using var context = await OpenAsync();

        RaskValidationException refused;
        using (Tenant.Use(Acme))
        {
            context.Add(Lane.Between("Budapest", "Vienna"));
            context.Add(Lane.Between("Gyor", "Graz").With(name: "Budapest–Vienna"));
            refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(["Name"], Assert.Single(refused.Failures).Fields);
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task The_check_and_the_save_share_one_transaction_that_is_closed_either_way()
    {
        await using var context = await OpenAsync();

        using (Tenant.Use(Acme))
        {
            context.Add(Lane.Between("Budapest", "Vienna"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            var afterASave = context.Database.CurrentTransaction;
            context.Add(Lane.Between("Gyor", "Graz").With(name: "Budapest–Vienna"));
            await Assert.ThrowsAsync<RaskValidationException>(
                () => context.SaveChangesAsync(TestContext.Current.CancellationToken));

            Assert.Null(afterASave);
            Assert.Null(context.Database.CurrentTransaction);
        }

        Assert.Equal(1, await RowsAsync());
    }

    [Fact]
    public async Task A_transaction_the_caller_opened_is_used_and_left_for_the_caller_to_end()
    {
        await using var context = await OpenAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        using (Tenant.Use(Acme))
        {
            context.Add(Lane.Between("Budapest", "Vienna"));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Same(transaction, context.Database.CurrentTransaction);
        await transaction.RollbackAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, await RowsAsync());
    }

    [Fact]
    public async Task A_unique_index_with_no_message_is_not_checked_so_its_duplicate_goes_through_here()
    {
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        // Slug is IsUnique() with no message: there is no failure to raise, so nothing asks. Where the index
        // exists the database refuses; here it was dropped.
        await SaveAsync(Acme, Lane.Between("Gyor", "Graz").With(slug: "budapest-vienna"));

        Assert.Equal(2, await RowsAsync());
    }

    [Fact]
    public async Task A_null_in_one_of_the_rules_columns_is_not_checked()
    {
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));
        await using var context = await OpenAsync();
        var lane = context.Model.FindEntityType(typeof(Lane))!;

        IReadOnlyList<Rask.Wire.FieldFailure> failures;
        using (Tenant.Across())
        {
            failures = await DeclaredUniqueRules.CheckAsync(
                context,
                lane,
                name => name switch { "Name" => "Budapest–Vienna", "TenantId" => null, "Code" => "X", "Origin" => "X", "Target" => "X", _ => null },
                selfKey: null,
                TestContext.Current.CancellationToken);
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void A_filter_that_only_rules_out_its_own_nulls_counts_as_none_and_any_other_filter_skips_the_rule()
    {
        using var context = new BerthContext(new DbContextOptionsBuilder<BerthContext>().UseSqlite("Data Source=:memory:").Options);
        var berth = context.Model.FindEntityType(typeof(Berth))!;

        var asked = DeclaredUniqueRules.Rules(berth).Select(ColumnsOf).ToList();

        Assert.Equal(["Code,TenantId"], asked);
    }

    private static string ColumnsOf(IIndex index) => string.Join(',', index.Properties.Select(p => p.Name));

    private static async Task<int> SaveAsync(int tenant, Lane lane)
    {
        using (Tenant.Use(tenant))
        {
            await lane.Save(cancellationToken: TestContext.Current.CancellationToken);
            return lane.Id;
        }
    }

    private async Task<int> RowsAsync()
    {
        await using var context = await OpenAsync();
        return (await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Lane")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();
    }

    private Task<DomainContext> OpenAsync() =>
        _app.GetRequiredService<IDbContextFactory<DomainContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
}
