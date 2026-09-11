using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Auth.Tests;

/// <summary>
///     The claim row is what makes "the first account is the administrator" single-winner. These test it
///     directly rather than through registration.
/// </summary>
/// <remarks>
///     Going through <c>RegisterAsync</c> cannot pin this. Creating an account is slow enough that the
///     first racer has committed its claim long before the second one reads, so a naive read-then-write
///     would pass too — which it was verified to do. Calling the store straight after a barrier is what
///     actually puts several callers inside the window at once.
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class InstanceClaimStoreTests
{
    [Fact]
    public async Task An_unclaimed_instance_reports_unclaimed()
    {
        await using var harness = new AuthHarness();

        Assert.False(await Store(harness).IsClaimedAsync());
    }

    [Fact]
    public async Task Claiming_makes_it_claimed()
    {
        await using var harness = new AuthHarness();
        var store = Store(harness);

        Assert.True(await store.TryClaimAsync("user-1"));
        Assert.True(await store.IsClaimedAsync());
    }

    [Fact]
    public async Task A_second_claim_loses()
    {
        await using var harness = new AuthHarness();
        var store = Store(harness);

        Assert.True(await store.TryClaimAsync("user-1"));
        Assert.False(await store.TryClaimAsync("user-2"));
    }

    /// <summary>
    ///     Many callers inside the window at once: exactly one wins.
    /// </summary>
    /// <remarks>
    ///     This is the test with teeth. It was checked against a deliberately broken store — one that
    ///     reads "is it claimed?" and then writes, with no database-level guarantee — and it fails there,
    ///     which is the only reason to trust it when it passes.
    /// </remarks>
    [Fact]
    public async Task Exactly_one_of_many_simultaneous_claims_wins()
    {
        await using var harness = new AuthHarness();
        var store = Store(harness);

        const int racers = 8;
        using var gate = new Barrier(racers);

        var won = await Task.WhenAll(Enumerable.Range(0, racers).Select(i => Task.Run(() =>
        {
            // Nothing between the barrier and the claim, so every racer is genuinely in the window.
            gate.SignalAndWait();
            return store.TryClaimAsync($"user-{i}");
        })));

        Assert.Equal(1, won.Count(w => w));
    }

    /// <summary>
    ///     A write that fails for any reason other than losing the race is an error, not a loss.
    /// </summary>
    /// <remarks>
    ///     On a client-server database a dropped connection or a deadlock victim surfaces as the same
    ///     <see cref="DbUpdateException" /> the constraint violation does. Swallowing it would leave the
    ///     instance unclaimed and tell the first registrant they are an ordinary user.
    /// </remarks>
    [Fact]
    public async Task A_failed_write_that_left_the_instance_unclaimed_is_rethrown()
    {
        await WithStoreAsync(new FailBeforeWriteInterceptor(), async store =>
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => store.TryClaimAsync("user-1"));
            Assert.False(await store.IsClaimedAsync());
        });
    }

    /// <summary>
    ///     A write that committed and then reported an error is a win, not a loss.
    /// </summary>
    /// <remarks>
    ///     A connection that drops after COMMIT, or a retrying execution strategy re-running the insert into
    ///     its own constant key, both surface as <see cref="DbUpdateException" /> with the caller's row in
    ///     the table. Reporting a loss there would leave the instance claimed by an account that never got
    ///     the administrator role — and unclaimable by anyone else.
    /// </remarks>
    [Fact]
    public async Task A_write_that_committed_before_its_error_still_wins()
    {
        await WithStoreAsync(new FailAfterCommitInterceptor(), async store =>
        {
            Assert.True(await store.TryClaimAsync("user-1"));
            Assert.True(await store.IsClaimedAsync());
        });
    }

    private static IInstanceClaimStore Store(AuthHarness harness) =>
        harness.Services.GetRequiredService<IInstanceClaimStore>();

    private static async Task WithStoreAsync(SaveChangesInterceptor interceptor, Func<IInstanceClaimStore, Task> test)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"rask-auth-claim-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<AuthDbContext>(o => o
            .UseSqlite($"Data Source={dbPath}")
            .AddInterceptors(interceptor));

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<AuthDbContext>>();

        try
        {
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
            }

            await test(new InstanceClaimStore<AuthDbContext>(factory, TimeProvider.System));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(dbPath);
        }
    }

    // A server that refused the write: the exception a real provider raises for a dropped connection, thrown
    // before anything reaches the table.
    private sealed class FailBeforeWriteInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("simulated: the connection dropped before the write");
    }

    // A write whose acknowledgement was lost: SavedChanges runs after EF has committed its own transaction, so
    // the row is in the table when the error arrives.
    private sealed class FailAfterCommitInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("simulated: the connection dropped after the commit");
    }
}
