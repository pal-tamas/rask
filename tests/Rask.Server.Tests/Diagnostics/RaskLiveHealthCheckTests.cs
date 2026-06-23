using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rask.Core;
using Rask.Core.Components;
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

    private static async Task<HealthStatus> Status(LiveSessionStore store)
    {
        var result = await new RaskLiveHealthCheck(store)
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
        protected override RenderResult Render() => new Span();
    }
}
