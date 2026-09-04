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

    private static IInstanceClaimStore Store(AuthHarness harness) =>
        harness.Services.GetRequiredService<IInstanceClaimStore>();
}
