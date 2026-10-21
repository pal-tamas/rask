using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     Two saves that each check "is this pistol still free?" through a read face and then take it. Inside
///     <c>Db.Transaction</c> at <see cref="IsolationLevel.Serializable" /> the check runs in the save's own transaction,
///     so the database refuses or serialises the second — exactly one claim lands. At the default isolation both do,
///     which is the race this exists to close.
/// </summary>
internal static class PistolRace
{
    private static readonly TimeSpan BothChecked = TimeSpan.FromSeconds(30);

    /// <summary>Both claims, and what each ended in (null when it completed).</summary>
    internal static async Task<(int Claims, Exception?[] Failures)> Run(
        Func<TenancyDbContext> open, Action<DbContextOptionsBuilder> provider, IsolationLevel? isolation)
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
            var pistol = Guid.NewGuid();
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var failures = await Task.WhenAll(
                Task.Run(() => Claim(pistol, "ada@example.com", first, second.Task, isolation)),
                Task.Run(() => Claim(pistol, "bob@example.com", second, first.Task, isolation)));

            await using var db = open();
            var claims = await db.Members.CountAsync(m => m.Owner == pistol, TestContext.Current.CancellationToken);
            return (claims, failures);
        }
        finally
        {
            Db.Reset();
        }
    }

    private static async Task<Exception?> Claim(
        Guid pistol, string holder, TaskCompletionSource mine, Task theirs, IsolationLevel? isolation)
    {
        try
        {
            await Db.Transaction(async () =>
            {
                var taken = await Member.Count(m => m.Owner == pistol, TestContext.Current.CancellationToken);

                // Both have looked before either writes: the interleaving that let two saves take one pistol.
                mine.TrySetResult();
                await theirs.WaitAsync(BothChecked, TestContext.Current.CancellationToken);

                if (taken == 0)
                {
                    await GeneratedModelWrites.Create(Member.For(holder, pistol), cancellationToken: TestContext.Current.CancellationToken);
                }
            }, isolation, TestContext.Current.CancellationToken);

            return null;
        }
#pragma warning disable CA1031 // the test asserts on what each claim ended in, whatever it was
        catch (Exception e)
#pragma warning restore CA1031
        {
            // A refused claim must not leave its partner waiting for a check it will never make.
            mine.TrySetResult();
            return e;
        }
    }
}

[Collection(PostgresCollection.Name)]
public sealed class PostgresDbTransactionRaceTests
{
    private const string Schema = "tenancy";

    [Fact]
    public async Task Serializable_lets_one_claim_land_and_retries_the_other_into_seeing_it()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await Reset();

        var (claims, failures) = await PistolRace.Run(Open, o => o.UseRaskPostgresAt(Postgres.Required), IsolationLevel.Serializable);

        // Retrying is on by default, and 40001 is transient: the loser runs again, sees the pistol taken, writes nothing.
        Assert.Equal(1, claims);
        Assert.All(failures, Assert.Null);
    }

    [Fact]
    public async Task Serializable_without_retrying_refuses_the_second_claim()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await Reset();

        var (claims, failures) = await PistolRace.Run(
            Open, o => o.UseRaskPostgresAt(Postgres.Required, p => p.Retry.Enabled = false), IsolationLevel.Serializable);

        Assert.Equal(1, claims);
        Assert.Single(failures, f => f is not null);
    }

    [Fact]
    public async Task At_the_default_isolation_both_claims_land()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await Reset();

        var (claims, failures) = await PistolRace.Run(Open, o => o.UseRaskPostgresAt(Postgres.Required), isolation: null);

        // Read committed: what the check saw was true when it ran and false by the time either wrote.
        Assert.Equal(2, claims);
        Assert.All(failures, Assert.Null);
    }

    private static async Task Reset()
    {
        await using var db = Open();
        await Postgres.ResetSchemaAsync(db, Schema);
    }

    private static TenancyDbContext Open() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseRaskPostgresAt(Postgres.Required).Options);
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDbTransactionRaceTests : IAsyncLifetime
{
    private const string DatabaseName = "rask_db_transaction_race";

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
    public async Task Serializable_lets_one_claim_land_and_retries_the_deadlock_victim_into_seeing_it()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var (claims, failures) = await PistolRace.Run(
            Open, o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName)), IsolationLevel.Serializable);

        // Both hold the range they counted; each insert waits on the other's; 1205 picks a victim, which is retried.
        Assert.Equal(1, claims);
        Assert.All(failures, Assert.Null);
    }

    [Fact]
    public async Task Serializable_without_retrying_refuses_the_second_claim()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var (claims, failures) = await PistolRace.Run(
            Open,
            o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName), s => s.Retry.Enabled = false),
            IsolationLevel.Serializable);

        Assert.Equal(1, claims);
        Assert.Single(failures, f => f is not null);
    }

    [Fact]
    public async Task At_the_default_isolation_both_claims_land()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);

        var (claims, failures) = await PistolRace.Run(
            Open, o => o.UseRaskSqlServerAt(SqlServer.Database(DatabaseName)), isolation: null);

        Assert.Equal(2, claims);
        Assert.All(failures, Assert.Null);
    }

    private static TenancyDbContext Open() =>
        new(new DbContextOptionsBuilder<TenancyDbContext>().UseRaskSqlServerAt(SqlServer.Database(DatabaseName)).Options);
}
