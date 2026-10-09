using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Rask.Cqrs;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// On SQL Server, over a table whose unique index was never created: the rule the aggregate declares —
/// filtered <c>[TenantId] IS NOT NULL</c>, as the real one is — is asked as a query and refuses the duplicate.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDeclaredRulesTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_declared_rules";

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await using var legacy = NewLegacy();
        await legacy.Database.EnsureDeletedAsync();
        await legacy.Database.EnsureCreatedAsync();
        legacy.AddRange(new LegacyTenant { Name = "Acme" }, new LegacyTenant { Name = "Globex" });
        await legacy.SaveChangesAsync();

        // What the model declares and this database does not have.
        await legacy.Database.ExecuteSqlAsync($"DROP INDEX [IX_Destinations_Name_TenantId] ON [Destinations]");
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var legacy = NewLegacy();
            await legacy.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task A_duplicate_in_one_tenant_is_refused_by_the_check_and_another_tenant_may_hold_it()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        var connection = SqlServer.Database(DatabaseName);
        var services = new ServiceCollection();
        services.AddRaskCqrs();
        services.AddRaskData<AdoptedDomainContext>();
        services.AddDeclaredRuleChecks();
        services.AddDbContextFactory<AdoptedDomainContext>((sp, o) => o
            .UseSqlServer(connection)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));
        services.AddDbContextFactory<RaskReadDbContext>(o => o.UseSqlServer(connection));
        await using var app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(app);

        try
        {
            RaskValidationException refused;
            using (Tenant.Use(1))
            {
                await Destination.Named("Budapest").Save(cancellationToken: TestContext.Current.CancellationToken);
                refused = await Assert.ThrowsAsync<RaskValidationException>(
                    () => Destination.Named("Budapest").Save(cancellationToken: TestContext.Current.CancellationToken));
            }

            using (Tenant.Use(2))
            {
                await Destination.Named("Budapest").Save(cancellationToken: TestContext.Current.CancellationToken);
            }

            var failure = Assert.Single(refused.Failures);
            Assert.Equal((Destination.NameTaken, "Name", "IX_Destinations_Name_TenantId"), (failure.Message, failure.Fields.Single(), failure.Source));
            Assert.Null(refused.InnerException);
            await using var legacy = NewLegacy();
            Assert.Equal(2, await legacy.Destinations.CountAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            Db.Reset();
        }
    }

    private static AdoptedLegacyContext NewLegacy() =>
        new(new DbContextOptionsBuilder<AdoptedLegacyContext>().UseSqlServer(SqlServer.Database(DatabaseName)).Options);
}
