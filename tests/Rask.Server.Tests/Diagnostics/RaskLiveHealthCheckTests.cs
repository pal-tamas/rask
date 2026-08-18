using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rask.Core;
using Rask.Core.Components;
using Rask.Html.Components;
using Rask.Server.Diagnostics;

namespace Rask.Server.Tests.Diagnostics;

public class RaskLiveHealthCheckTests
{
    [Fact]
    public async Task Uncapped_IsAlwaysHealthy()
    {
        var store = NewStore();
        store.MaxSessions = 0;
        store.Create(_ => new BasicComponent());

        Assert.Equal(HealthStatus.Healthy, await Status(store));
    }

    [Fact]
    public async Task BelowEightyPercent_IsHealthy()
    {
        var store = NewStore();
        store.MaxSessions = 5;
        // 3/5 = 60% — comfortably below the degraded threshold.
        for (var i = 0; i < 3; i++)
        {
            store.Create(_ => new BasicComponent());
        }

        Assert.Equal(HealthStatus.Healthy, await Status(store));
    }

    [Fact]
    public async Task AtOrAboveEightyPercent_IsDegraded()
    {
        var store = NewStore();
        store.MaxSessions = 5;
        // 4/5 = 80% — at the degraded threshold.
        for (var i = 0; i < 4; i++)
        {
            store.Create(_ => new BasicComponent());
        }

        Assert.Equal(HealthStatus.Degraded, await Status(store));
    }

    [Fact]
    public async Task AtCapacity_IsUnhealthy()
    {
        var store = NewStore();
        store.MaxSessions = 5;
        for (var i = 0; i < 5; i++)
        {
            store.Create(_ => new BasicComponent());
        }

        Assert.Equal(HealthStatus.Unhealthy, await Status(store));
    }

    // ---- Memory, which outranks the session count in both directions -------------------------------
    //
    // None of these could be written before the reading became a seam: the only input was whatever the
    // host happened to be doing, so the thresholds below — both load-bearing in production — had no
    // coverage at all.

    [Fact]
    public async Task AtTheMemoryCeiling_IsUnhealthy_EvenWithAnEmptyStore()
    {
        var store = NewStore();
        store.MaxSessions = 100;

        Assert.Equal(HealthStatus.Unhealthy, await Status(store, memoryLoad: 0.95));
    }

    [Fact]
    public async Task NearTheMemoryCeiling_IsDegraded_EvenWithAnEmptyStore()
    {
        // The point of watching memory at all: a session's cost is a property of the PAGE, so a host
        // well under its session cap can still be in trouble.
        var store = NewStore();
        store.MaxSessions = 100;

        Assert.Equal(HealthStatus.Degraded, await Status(store, memoryLoad: 0.85));
    }

    [Fact]
    public async Task AnUncappedHostStillDegradesUnderMemoryPressure()
    {
        // Uncapped means "no session limit", not "never unhealthy".
        var store = NewStore();
        store.MaxSessions = 0;

        Assert.Equal(HealthStatus.Degraded, await Status(store, memoryLoad: 0.85));
        Assert.Equal(HealthStatus.Unhealthy, await Status(store, memoryLoad: 0.95));
    }

    [Fact]
    public async Task AnUnreadableMemoryPositionIsNotAnUnhealthyOne()
    {
        // MemoryLoad() returns 0 when the runtime won't say, deliberately: a host must not shed load
        // because it could not measure itself.
        var store = NewStore();
        store.MaxSessions = 100;

        Assert.Equal(HealthStatus.Healthy, await Status(store, memoryLoad: 0.0));
    }

    // The memory reading defaults to PINNED, not to the real one. The check judges memory first and
    // lets it outrank the session count — correct for the product, and fatal for a test about the
    // SESSION branch, because a machine already past DegradedMemoryLoad makes every one of these
    // report Degraded regardless of the store. That is what made the full local gate go red on
    // unrelated changes (#732). Tests that are about memory pass their own value.
    private static async Task<HealthStatus> Status(LiveSessionStore store, double memoryLoad = 0.0)
    {
        var result = await new RaskLiveHealthCheck(store) { MemoryLoadReader = () => memoryLoad }
            .CheckHealthAsync(new HealthCheckContext());
        return result.Status;
    }

    private static LiveSessionStore NewStore()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        return new LiveSessionStore(sp.GetRequiredService<IServiceScopeFactory>());
    }

    private sealed class BasicComponent : Component
    {
        protected override Component? Render() => new Span();
    }
}
