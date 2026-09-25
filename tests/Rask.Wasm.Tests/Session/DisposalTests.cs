using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Globalization;
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
    public void Disposing_the_session_unsubscribes_from_the_user_providers_Changed()
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

    // #1093: IRaskCulture is a root singleton on WASM, so it outlives every session. A handler left attached keeps
    // the disposed tree reachable and re-renders it on the next language switch.
    [Fact]
    public void Disposing_the_session_unsubscribes_from_CultureChanged()
    {
        // Process-wide, and deliberately not reset: it only decides whether a session LOOKS for a culture
        // service (see RaskCulture.IsEnabled), so leaving it on cannot change what a parallel test renders.
        RaskCulture.IsEnabled = true;
        var culture = new CountingCulture();
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        services.AddSingleton<IRaskCulture>(culture);
        var sp = services.BuildServiceProvider();

        var session = new WasmLiveSession(new MiniApp(), sp, LiveDiffMode.Auto);
        Assert.Equal(1, culture.SubscriberCount);

        session.Dispose();

        Assert.Equal(0, culture.SubscriberCount);
    }

    [Fact]
    public void The_download_sinks_orphaned_stagings_are_bounded()
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
    public void A_download_staged_then_pulled_round_trips()
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
