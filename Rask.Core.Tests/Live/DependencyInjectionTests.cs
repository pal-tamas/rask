using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Core.Tests.Live;

public class DependencyInjectionTests
{
    [Fact]
    public void ConstructorInjection_ResolvesServicesViaGeneratedFactory()
    {
        var services = new ServiceCollection()
            .AddRask()
            .AddSingleton<IGreeter>(new FixedGreeter("world"))
            .BuildServiceProvider();

        var store = services.GetRequiredService<LiveSessionStore>();
        var session = store.Create(sp => ActivatorUtilities.CreateInstance<GreetingComponent>(sp));

        var html = session.View.RenderAsLiveRoot(session.Services);

        Assert.Equal("<span>hello, world</span>", html);
    }

    [Fact]
    public void GeneratedFactory_OutsideContext_ParameterlessComponent_StillNew()
    {
        // global::Rask.Core.Tests.Live.Components.ParameterlessComponent() should compile and return a fresh instance with no context.
        var instance = Components.ParameterlessComponent();
        Assert.NotNull(instance);
        Assert.Equal("<span>plain</span>", instance.ToHtml());
    }

    [Fact]
    public void GeneratedFactory_OutsideContext_DependencyComponent_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Components.GreetingComponent());

    [Fact]
    public void GeneratedFactory_InsideContext_ResolvesViaActivatorUtilities()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGreeter>(new FixedGreeter("ctx"))
            .BuildServiceProvider();

        var root = new StubComponent(new Span(null));
        using var ctx = LiveRenderContext.Begin(root, services);

        var instance = Components.GreetingComponent();

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
        var session = store.Create(sp => ActivatorUtilities.CreateInstance<TrackerComponent>(sp));
        var tracker = ((TrackerComponent)session.View).Tracker;

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
        var s1 = store.Create(sp => ActivatorUtilities.CreateInstance<GreetingComponent>(sp));
        var s2 = store.Create(sp => ActivatorUtilities.CreateInstance<GreetingComponent>(sp));

        Assert.NotSame(s1.View, s2.View);
        Assert.NotEqual(s1.Id, s2.Id);
    }

    public interface IGreeter
    {
        string Name { get; }
    }

    public sealed class FixedGreeter : IGreeter
    {
        public FixedGreeter(string name) => Name = name;
        public string Name { get; }
    }

    public sealed class GreetingComponent : Component
    {
        private readonly IGreeter _greeter;
        public GreetingComponent(IGreeter greeter) => _greeter = greeter;

        public override Component Render() =>
            new Span(null, new Text($"hello, {_greeter.Name}"));
    }

    public sealed class ParameterlessComponent : Component
    {
        public override Component Render() => new Span(null, new Text("plain"));
    }

    public sealed class ScopedTracker : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    public sealed class TrackerComponent : Component
    {
        public TrackerComponent(ScopedTracker tracker) => Tracker = tracker;
        public ScopedTracker Tracker { get; }
        public override Component Render() => new Raw("<x/>");
    }
}
