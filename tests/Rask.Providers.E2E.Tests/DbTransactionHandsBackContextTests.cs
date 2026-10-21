using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     One <c>db:</c> context through two <c>Db.Transaction</c> calls and a save outside both, on a provider that retries:
///     each transaction hands it back on its own connection and in no transaction, so the next one admits it again.
/// </summary>
internal static class HandBackRun
{
    /// <summary>The members left after a create, a delete and a create outside, all through one context.</summary>
    internal static async Task<List<string>> Run(Func<TenancyDbContext> open, Action<DbContextOptionsBuilder> provider)
    {
        var services = new ServiceCollection();
        services.AddRaskData<TenancyDbContext>();
        services.AddDbContextFactory<TenancyDbContext>((sp, o) =>
        {
            provider(o);
            o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
        services.AddDbContextFactory<RaskReadDbContext>(provider);
        await using var app = services.BuildServiceProvider(validateScopes: true);
        Db.Configure(app);

        try
        {
            await using var db = open();
            var ada = Member.For("ada@example.com", owner: null);

            await Db.Transaction(async () =>
            {
                await GeneratedModelWrites.Create(ada, db: db, cancellationToken: TestContext.Current.CancellationToken);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }, cancellationToken: TestContext.Current.CancellationToken);
            await Db.Transaction(async () =>
            {
                await GeneratedModelWrites.Delete<Member>(ada.Id, version: null, db: db, cancellationToken: TestContext.Current.CancellationToken);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }, cancellationToken: TestContext.Current.CancellationToken);
            db.Add(Member.For("bob@example.com", owner: null));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            Assert.Null(db.Database.CurrentTransaction);
            await using var check = open();
            return await check.Members.Select(m => m.Email).ToListAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            Db.Reset();
        }
    }
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresDbTransactionHandsBackContextTests
{
    [Fact]
    public async Task One_context_runs_two_transactions_and_then_a_save_outside_any()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using (var reset = Open())
        {
            await Postgres.ResetSchemaAsync(reset, "tenancy");
        }

        var left = await HandBackRun.Run(Open, o => o.UseRaskPostgresAt(Postgres.Required));

        Assert.Equal(["bob@example.com"], left);
    }

    private static TenancyDbContext Open() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseRaskPostgresAt(Postgres.Required).Options);
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDbTransactionHandsBackContextTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_db_transaction_hand_back";

    public async ValueTask InitializeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = Open();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            await using var db = Open();
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task One_context_runs_two_transactions_and_then_a_save_outside_any()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var left = await HandBackRun.Run(Open, o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName)));

        Assert.Equal(["bob@example.com"], left);
    }

    private static TenancyDbContext Open() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);
}
