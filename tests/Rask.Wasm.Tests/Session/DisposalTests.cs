using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Wasm.Files;

#pragma warning disable RASK014 // test-defined Component subclass has no generated factory
#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Wasm.Tests.Session;

// M10: WasmLiveSession.Dispose must unsubscribe from IUserProvider.Changed (the provider can
// outlive the session, so a dangling handler would fire OnUserChanged on a disposed _lock).
// M12: WasmDownloadSink must bound retained un-pulled stagings instead of leaking byte[] for the
// page lifetime.
public class DisposalTests
{
    [Fact]
    public void Dispose_UnsubscribesFromUserProviderChanged()
    {
        var provider = new CountingUserProvider();
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        services.AddSingleton<IUserProvider>(provider);
        var sp = services.BuildServiceProvider();

        var session = new WasmLiveSession(new MiniApp(), sp, LiveDiffMode.Auto);
        Assert.Equal(1, provider.SubscriberCount); // session subscribed in the ctor

        session.Dispose();

        Assert.Equal(0, provider.SubscriberCount); // ...and unsubscribed on dispose
    }

    [Fact]
    public void DownloadSink_OrphanedStagings_AreBounded()
    {
        var sink = new WasmDownloadSink();

        // Stage far more than the cap without ever pulling — the orphaned-download leak case.
        for (var i = 0; i < 100; i++)
        {
            sink.Stage($"f{i}.bin", new[] { (byte)i }, null);
        }

        Assert.True(sink.RetainedCount <= 16,
            $"retained stagings must be bounded; was {sink.RetainedCount}");
    }

    [Fact]
    public void DownloadSink_StageThenPull_RoundTrips()
    {
        // The eviction bound must not disturb the normal one-stage-one-pull flow.
        var sink = new WasmDownloadSink();
        var bytes = new byte[] { 1, 2, 3 };
        sink.Stage("a.bin", bytes, null);
        Assert.True(sink.TryConsume(out var pending));
        var token = pending!.Token!;

        Assert.Equal(bytes, sink.Pull(token));
        Assert.Empty(sink.Pull(token)); // drained
        Assert.Equal(0, sink.RetainedCount);
    }

    private sealed class MiniApp : Component
    {
        protected override Component? Render() => null;
    }

    private sealed class CountingUserProvider : IUserProvider
    {
        private Action? _changed;

        public int SubscriberCount => _changed?.GetInvocationList().Length ?? 0;

        public ClaimsPrincipal Current { get; } = new(new ClaimsIdentity());

        public event Action? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }
    }
}
