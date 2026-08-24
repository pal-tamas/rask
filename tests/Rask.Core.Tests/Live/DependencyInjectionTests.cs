using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Server;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public partial class DependencyInjectionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void ConstructorInjection_ResolvesServicesViaGeneratedFactory()
    {
        var services = new ServiceCollection()
            .AddRask()
            .AddSingleton<IGreeter>(new FixedGreeter("world"))
            .BuildServiceProvider();

        var store = services.GetRequiredService<LiveSessionStore>();
        var session = store.Create(sp => ActivatorUtilities.CreateInstance<global::Rask.Core.Tests.Live.GreetingComponent>(sp));

        var html = session.View.RenderAsLiveRoot(session.Services);

        Assert.Equal("<span>hello, world</span>", html);
    }

    [Fact]
    public void Chain_OutsideContext_ParameterlessComponent_StillConstructs()
    {
        // A component that injects nothing needs no provider, so its chain works with no context at all.
        Component instance = ParameterlessComponent;
        Assert.NotNull(instance);
        Assert.Equal("<span>plain</span>", instance.ToHtml());
    }

    [Fact]
    public void Chain_OutsideContext_DependencyComponent_Throws() =>
        // Nothing to resolve the constructor argument from — the chain says so rather than handing back
        // a half-built component that fails later, somewhere else.
        Assert.Throws<InvalidOperationException>(() => { Component _ = GreetingComponent; });

    [Fact]
    public void Chain_InsideContext_ResolvesViaActivatorUtilities()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGreeter>(new FixedGreeter("ctx"))
            .BuildServiceProvider();

        var root = new StubComponent(Span);
        using var ctx = LiveRenderContext.Begin(root, services);

        Component instance = GreetingComponent;

        Assert.Equal("<span>hello, ctx</span>", instance.ToHtml());
    }

    [Fact]
    public void RemoveSession_DisposesScopedServices()
    {
        var services = new ServiceCollection()
            .AddRask()
            .AddScoped<ScopedTracker>()
            .BuildServiceProvider();

        var store = services.GetRequiredService<LiveSessionStore>();
        var session = store.Create(sp => ActivatorUtilities.CreateInstance<global::Rask.Core.Tests.Live.TrackerComponent>(sp));
        var tracker = ((global::Rask.Core.Tests.Live.TrackerComponent)session.View).Tracker;

        Assert.False(tracker.Disposed);

        store.Remove(session.Id);

        Assert.True(tracker.Disposed);
    }

    [Fact]
    public void EachSession_GetsDistinctComponentInstance()
    {
        var services = new ServiceCollection()
            .AddRask()
            .AddSingleton<IGreeter>(new FixedGreeter("x"))
            .BuildServiceProvider();

        var store = services.GetRequiredService<LiveSessionStore>();
        var s1 = store.Create(sp => ActivatorUtilities.CreateInstance<global::Rask.Core.Tests.Live.GreetingComponent>(sp));
        var s2 = store.Create(sp => ActivatorUtilities.CreateInstance<global::Rask.Core.Tests.Live.GreetingComponent>(sp));

        Assert.NotSame(s1.View, s2.View);
        Assert.NotEqual(s1.Id, s2.Id);
    }

}

// Top-level rather than nested in the test class, and that is load-bearing twice over. A markup host
// that DECLARES a component is given no entries at all (its own nested names would collide with them),
// so a component nested here could not be reached by a chain from the very test that needs one. Out
// here it gets an entry — which then shadows the type name inside the class body, so the three
// `ActivatorUtilities.CreateInstance<…>` calls below name their type in full.
public interface IGreeter
{
    string Name { get; }
}

public sealed class FixedGreeter : IGreeter
{
    public FixedGreeter(string name) => Name = name;
    public string Name { get; }
}

public sealed partial class GreetingComponent : Component
{
    private readonly IGreeter _greeter;
    public GreetingComponent(IGreeter greeter) => _greeter = greeter;

    protected override Component? Render() =>
        Span[Text.Value($"hello, {_greeter.Name}")];
}

public sealed partial class ParameterlessComponent : Component
{
    protected override Component? Render() => Span[Text.Value("plain")];
}

public sealed class ScopedTracker : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

public sealed partial class TrackerComponent : Component
{
    public TrackerComponent(ScopedTracker tracker) => Tracker = tracker;
    public ScopedTracker Tracker { get; }
    protected override Component? Render() => Raw.Value("<x/>");
}
