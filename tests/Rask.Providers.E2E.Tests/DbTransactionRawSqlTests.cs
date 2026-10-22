using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     The app's own context, from its factory, running raw SQL inside <c>Db.Transaction</c> and then handed in as
///     <c>db:</c>: the raw write and read run inside the transaction, the <c>db:</c> call joins it, a rollback takes the
///     raw write back, and the context comes out on its own connection.
/// </summary>
internal static class RawSqlRun
{
    /// <summary>What a raw count inside saw, and the members left after a committed and a rolled-back block.</summary>
    internal static async Task<(int Seen, List<string> Left)> Run(Action<DbContextOptionsBuilder> provider)
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
            var factory = app.GetRequiredService<IDbContextFactory<TenancyDbContext>>();
            await using var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            await GeneratedModelWrites.Create(Member.For("ada@example.com", owner: null), cancellationToken: TestContext.Current.CancellationToken);
            var seen = -1;

            await Db.Transaction(async () =>
            {
                await GeneratedModelWrites.Create(Member.For("cy@example.com", owner: null), cancellationToken: TestContext.Current.CancellationToken);
                await db.Database.ExecuteSqlRawAsync(
                    """UPDATE "tenancy"."Member" SET "Email" = 'ada@renamed.example' WHERE "Email" = 'ada@example.com'""",
                    TestContext.Current.CancellationToken);
                seen = await db.Database.SqlQueryRaw<int>("""SELECT COUNT(*) AS "Value" FROM "tenancy"."Member" """)
                    .SingleAsync(TestContext.Current.CancellationToken);
                await GeneratedModelWrites.Create(Member.For("bob@example.com", owner: null), db: db, cancellationToken: TestContext.Current.CancellationToken);
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }, cancellationToken: TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
            {
                await db.Database.ExecuteSqlRawAsync("""DELETE FROM "tenancy"."Member" """, TestContext.Current.CancellationToken);
                throw new InvalidOperationException("roll back");
            }, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Null(db.Database.CurrentTransaction);
            await using var check = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            var left = await check.Members.Select(m => m.Email).ToListAsync(TestContext.Current.CancellationToken);
            return (seen, [.. left.Order(StringComparer.Ordinal)]);
        }
        finally
        {
            Db.Reset();
        }
    }
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresDbTransactionRawSqlTests
{
    [Fact]
    public async Task Raw_sql_on_the_apps_context_runs_inside_and_its_db_call_joins()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using (var reset = new TenancyDbContext(
            new DbContextOptionsBuilder<TenancyDbContext>().UseRaskPostgresAt(Postgres.Required).Options))
        {
            await Postgres.ResetSchemaAsync(reset, "tenancy");
        }

        var (seen, left) = await RawSqlRun.Run(o => o.UseRaskPostgresAt(Postgres.Required));

        Assert.Equal(2, seen);
        Assert.Equal(["ada@renamed.example", "bob@example.com", "cy@example.com"], left);
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDbTransactionRawSqlTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_db_transaction_raw_sql";

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
    public async Task Raw_sql_on_the_apps_context_runs_inside_and_its_db_call_joins()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var (seen, left) = await RawSqlRun.Run(o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName)));

        Assert.Equal(2, seen);
        Assert.Equal(["ada@renamed.example", "bob@example.com", "cy@example.com"], left);
    }

    private static TenancyDbContext Open() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);
}
