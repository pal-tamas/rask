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
        var session = store.Create(sp => ActivatorUtilities.CreateInstance<GreetingComponent>(sp));

        var html = session.View.RenderAsLiveRoot(session.Services);

        Assert.Equal("<span>hello, world</span>", html);
    }

    [Fact]
    public void GeneratedFactory_OutsideContext_ParameterlessComponent_StillNew()
    {
        // global::Rask.Core.Tests.Live.Generated.ParameterlessComponent() should compile and return a fresh instance with no context.
        var instance = Generated.ParameterlessComponent();
        Assert.NotNull(instance);
        Assert.Equal("<span>plain</span>", instance.ToHtml());
    }

    [Fact]
    public void GeneratedFactory_OutsideContext_DependencyComponent_Throws() =>
        Assert.Throws<InvalidOperationException>(() => Generated.GreetingComponent());

    [Fact]
    public void GeneratedFactory_InsideContext_ResolvesViaActivatorUtilities()
    {
        var services = new ServiceCollection()
            .AddSingleton<IGreeter>(new FixedGreeter("ctx"))
            .BuildServiceProvider();

        var root = new StubComponent(Span());
        using var ctx = LiveRenderContext.Begin(root, services);

        var instance = Generated.GreetingComponent();

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

        protected override Component? Render() =>
            Span[Text.Value($"hello, {_greeter.Name}")];
    }

    public sealed class ParameterlessComponent : Component
    {
        protected override Component? Render() => Span[Text.Value("plain")];
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
        protected override Component? Render() => Raw.Value("<x/>");
    }
}
