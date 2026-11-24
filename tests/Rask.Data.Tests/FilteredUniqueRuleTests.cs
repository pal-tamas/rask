using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>
///     A unique rule filtered with <c>Where(…)</c>, asked as a query before the save on a schema that lacks the index:
///     over exactly the rows the filtered index would hold.
/// </summary>
/// <remarks>The partial index is DROPPED, so a refusal here is the check's, never the database's.</remarks>
[Collection(DataDbCollection.Name)]
public sealed class FilteredUniqueRuleTests : IAsyncLifetime
{
    private static readonly DateTime Revoked = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-filtered-rule-{Guid.NewGuid():N}.db");
    private ServiceProvider _app = null!;

    public async ValueTask InitializeAsync()
    {
        var connection = $"Data Source={_dbPath};Pooling=False";
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<NeutralContext>();
        services.AddDeclaredRuleChecks();
        services.AddDbContextFactory<NeutralContext>((sp, o) => o
            .UseSqlite(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlite(connection));
        _app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(_app);

        await using var context = await OpenAsync();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_PartnerGrant_Partner\"", TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task The_filtered_rule_is_one_the_check_asks()
    {
        await using var context = await OpenAsync();

        var rules = DeclaredUniqueRules.Rules(context.Model.FindEntityType(typeof(PartnerGrant))!);

        Assert.Contains(rules, r => r.Properties.Single().Name == nameof(PartnerGrant.Partner));
    }

    [Fact]
    public async Task A_second_live_grant_is_refused_by_the_check()
    {
        await SaveAsync(new PartnerGrant { Partner = "p1" });

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(new PartnerGrant { Partner = "p1" }));

        Assert.Equal("This partner already has a live grant.", Assert.Single(refused.Failures).Message);
        Assert.Null(refused.InnerException);
    }

    [Fact]
    public async Task A_live_grant_beside_revoked_ones_is_saved_because_the_filter_leaves_them_out()
    {
        await SaveAsync(new PartnerGrant { Partner = "p1", RevokedAt = Revoked });

        await SaveAsync(new PartnerGrant { Partner = "p1" });

        Assert.Equal(2, await RowsAsync());
    }

    [Fact]
    public async Task Revoked_grants_beside_a_live_one_are_saved_even_two_in_one_save()
    {
        await SaveAsync(new PartnerGrant { Partner = "p1" });

        await SaveAsync(new PartnerGrant { Partner = "p1", RevokedAt = Revoked }, new PartnerGrant { Partner = "p1", RevokedAt = Revoked });

        Assert.Equal(3, await RowsAsync());
    }

    [Fact]
    public async Task Two_live_grants_in_one_save_are_refused()
    {
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(new PartnerGrant { Partner = "p1" }, new PartnerGrant { Partner = "p1" }));

        Assert.Null(refused.InnerException);
        Assert.Equal(0, await RowsAsync());
    }

    private async Task SaveAsync(params PartnerGrant[] grants)
    {
        await using var context = await OpenAsync();
        context.AddRange(grants);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> RowsAsync()
    {
        await using var context = await OpenAsync();
        return await context.Set<PartnerGrant>().CountAsync(TestContext.Current.CancellationToken);
    }

    private Task<NeutralContext> OpenAsync() =>
        _app.GetRequiredService<IDbContextFactory<NeutralContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
}
