using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Live;

/// <summary>
/// The DI wiring behind <see cref="IPersistentState"/>. The bag is only useful if it is scoped to a live
/// session — a singleton would pool every visitor's declared state into one bag, which is a data leak, not
/// a bug in degree.
/// </summary>
public sealed class PersistentStateWiringTests
{
    [Fact]
    public void Resolves_from_a_session_scope()
    {
        using var host = RaskTestHost.Create<Shell>();
        var session = host.Store.Create(sp => new Shell(sp.GetRequiredService<IPersistentState>()));

        var state = session.Scope.ServiceProvider.GetRequiredService<IPersistentState>();

        Assert.NotNull(state);
    }

    /// <summary>
    /// The one that would be a security bug rather than a defect: one session must never read another's
    /// declared state.
    /// </summary>
    [Fact]
    public void Two_sessions_get_two_separate_bags()
    {
        using var host = RaskTestHost.Create<Shell>();
        var first = host.Store.Create(sp => new Shell(sp.GetRequiredService<IPersistentState>()));
        var second = host.Store.Create(sp => new Shell(sp.GetRequiredService<IPersistentState>()));

        var firstState = first.Scope.ServiceProvider.GetRequiredService<IPersistentState>();
        var secondState = second.Scope.ServiceProvider.GetRequiredService<IPersistentState>();

        Assert.NotSame(firstState, secondState);

        firstState.Persist("cart", "first-session-only");

        Assert.False(secondState.TryGet<string>("cart", out _));
        Assert.True(firstState.TryGet<string>("cart", out var mine));
        Assert.Equal("first-session-only", mine);
    }

    /// <summary>
    /// The interface and the concrete type must be the same object in a scope — the framework reads the
    /// version and the raw entries off the concrete one to build the handoff record, and would otherwise be
    /// reading a different bag than the app wrote to.
    /// </summary>
    [Fact]
    public void The_interface_and_the_concrete_bag_are_one_instance()
    {
        using var host = RaskTestHost.Create<Shell>();
        var session = host.Store.Create(sp => new Shell(sp.GetRequiredService<IPersistentState>()));

        var viaInterface = session.Scope.ServiceProvider.GetRequiredService<IPersistentState>();
        var viaConcrete = session.Scope.ServiceProvider.GetRequiredService<PersistentState>();

        Assert.Same(viaInterface, viaConcrete);

        viaInterface.Persist("tab", "reviews");
        Assert.Equal(1, viaConcrete.Version);
        Assert.True(viaConcrete.Entries.ContainsKey("tab"));
    }

    /// <summary>Constructor injection is the supported shape (a settable non-nullable prop would be RASK002).</summary>
    private sealed class Shell(IPersistentState state) : Component
    {
        protected override Component? Render()
        {
            state.Persist("rendered", true);
            return new Span();
        }
    }
}
