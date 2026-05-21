using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core;

namespace Rask.Example.Shared.Tests.Infrastructure;

// Parent wrapper that hosts a child component through its generator-emitted factory.
// Using a real Component subclass means the framework's render-walk, lifecycle dispatch,
// prop diff, and unmount fire on the child — same path that runs in production. Tests
// only stub the leaf services (HttpClient, IJSRuntime, Navigator/RouteState).
//
// [SkipFactory] keeps the generator from emitting a Components.LiveHost() factory in the
// test assembly that would collide with the LiveTicker static-import.
//
// Flip Mounted to false and re-render to drive the child through its OnUnmount path.
[SkipFactory]
internal sealed class LiveHost : Component
{
    private readonly Func<Component> _factory;
    private readonly IServiceProvider _services;

    public LiveHost(Func<Component> factory, IServiceProvider services)
    {
        _factory = factory;
        _services = services;
        Handle = new RecordingHandle();
        RenderHandle = Handle;
    }

    public LifecycleLog Log { get; } = new();

    public bool Mounted { get; set; } = true;

    // Recording handle exposed for tests asserting render/publish counts.
    public RecordingHandle Handle { get; }

    internal new string RenderAsLiveRoot() => base.RenderAsLiveRoot(_services);

    protected override Component Render() =>
        Mounted ? _factory() : (Component)Fragment();

    public static IServiceProvider Services(params (Type Service, object Instance)[] singletons)
    {
        var sc = new ServiceCollection();
        foreach (var (type, instance) in singletons)
        {
            sc.AddSingleton(type, instance);
        }

        return sc.BuildServiceProvider();
    }

    public static IServiceProvider Services(HttpClient http, IJSRuntime js)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton(http);
        sc.AddSingleton(js);
        return sc.BuildServiceProvider();
    }
}
