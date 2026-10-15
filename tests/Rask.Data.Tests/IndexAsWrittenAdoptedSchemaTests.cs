using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>
/// <see cref="Clerk" /> over a table somebody else made, in a host wired by hand the way an adopting app wires
/// it: the declared unique rules are asked as queries before the save, and the database's own index may not be
/// the one the model has.
/// </summary>
/// <remarks>
/// Every index of the table is DROPPED first, so a refusal with no inner exception is the check's.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class IndexAsWrittenAdoptedSchemaTests : IAsyncLifetime
{
    private static readonly Guid Acme = new("0f9d5a3c-7c1e-4d52-9a54-2b6f1f0c8e11");
    private static readonly Guid Globex = new("a1b2c3d4-0000-4000-8000-00000000beef");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-across-adopted-{Guid.NewGuid():N}.db");
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

        await using var context = await OpenAsync();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "DROP INDEX \"Clerk_username_key\"; DROP INDEX \"Clerk_badge_key\";",
            TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Db.Reset();
        await _app.DisposeAsync();
        File.Delete(_dbPath);
    }

    [Fact]
    public async Task A_username_another_tenant_holds_is_refused_by_the_check_before_the_save()
    {
        await SaveAsync(Acme, Clerk.Named("ada"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Globex, Clerk.Named("ada")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Clerk.UsernameTaken, failure.Message);
        Assert.Equal(["Username"], failure.Fields);
        Assert.Equal(Clerk.UsernameKey, failure.Source);
        Assert.Null(refused.InnerException);
        Assert.Equal(1, await RowsAsync());
    }

    [Fact]
    public async Task A_badge_another_tenant_holds_passes_the_check_because_that_rule_is_within_a_tenant()
    {
        await SaveAsync(Acme, Clerk.Named("ada", badge: "B-1"));

        await SaveAsync(Globex, Clerk.Named("bob", badge: "B-1"));

        Assert.Equal(2, await RowsAsync());
    }

    [Fact]
    public async Task A_database_index_left_global_is_recognised_while_the_model_names_the_tenant()
    {
        await using (var context = await OpenAsync())
        {
            // The table as an existing app has it: the badge unique among everybody's rows, under the name the
            // model gave it — while the model's index names the tenant.
            await context.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX \"Clerk_badge_key\" ON \"Clerk\" (\"Badge\")",
                TestContext.Current.CancellationToken);
        }

        await SaveAsync(Acme, Clerk.Named("ada", badge: "B-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Globex, Clerk.Named("bob", badge: "B-1")));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Clerk.BadgeTaken, failure.Message);
        Assert.Equal(["Badge"], failure.Fields);
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    private static async Task SaveAsync(Guid tenant, Clerk clerk)
    {
        using (Tenant.Use(tenant))
        {
            await clerk.Save(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    private async Task<int> RowsAsync()
    {
        await using var context = await OpenAsync();
        return (await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM Clerk")
            .ToListAsync(TestContext.Current.CancellationToken)).Single();
    }

    private Task<DomainContext> OpenAsync() =>
        _app.GetRequiredService<IDbContextFactory<DomainContext>>().CreateDbContextAsync(TestContext.Current.CancellationToken);
}
