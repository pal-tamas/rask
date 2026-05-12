using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Core.Tests.Live;
using static Rask.Core.Tags;

namespace Rask.Core.Tests.Routing;

[Collection("RouteRegistry")]
public class RouterTests
{
    private static (StubComponent view, RouteState state, IServiceProvider sp) BuildView(IReadOnlyList<Route> routes)
    {
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => Router(routes));
        return (view, state, sp);
    }

    private static string Render(StubComponent view, IServiceProvider sp) => view.RenderAsLiveRoot(sp);

    [Fact]
    public void Router_MatchedTopLevel_RendersPage()
    {
        var (view, state, sp) = BuildView(new[] { Route<HomePage>("/") });
        state.Path = "/";

        var html = Render(view, sp);

        Assert.Equal("<span>home</span>", html);
    }

    [Fact]
    public void Router_NoMatch_RendersEmptyFragment()
    {
        var (view, state, sp) = BuildView(new[] { Route<HomePage>("/") });
        state.Path = "/missing";

        var html = Render(view, sp);

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Router_BindsRouteValueOntoPageProperty()
    {
        var (view, state, sp) = BuildView(new[] { Route<UserPage>("/users/{id}") });
        state.Path = "/users/42";

        var html = Render(view, sp);

        Assert.Equal("<span>user:42</span>", html);
    }

    [Fact]
    public void Router_BindsQueryString_OntoPageProperty()
    {
        var (view, state, sp) = BuildView(new[] { Route<CounterPage>("/c") });
        state.Path = "/c";
        state.Query = new QueryCollection(new Dictionary<string, StringValues> { ["label"] = "tag" });

        var html = Render(view, sp);

        Assert.Equal("<span>tag:1</span>", html);
    }

    [Fact]
    public void Router_SameType_DifferentParams_PreservesInstanceState()
    {
        var (view, state, sp) = BuildView(new[] { Route<CounterPage>("/c/{label}") });
        state.Path = "/c/one";
        var first = Render(view, sp);

        state.Path = "/c/two";
        var second = Render(view, sp);

        Assert.Equal("<span>one:1</span>", first);
        Assert.Equal("<span>two:2</span>", second);
    }

    [Fact]
    public void Router_TypeSwap_DiscardsPreviousInstance()
    {
        var routes = new[] { Route<CounterPage>("/c/{label}"), Route<HomePage>("/h") };
        var (view, state, sp) = BuildView(routes);

        state.Path = "/c/x";
        var first = Render(view, sp);
        Assert.Equal("<span>x:1</span>", first);

        // Swap to a different page type — the previous CounterPage instance is no longer
        // referenced anywhere and is disposed at end of render.
        state.Path = "/h";
        var swapped = Render(view, sp);
        Assert.Equal("<span>home</span>", swapped);

        // Revisit /c/x — a fresh CounterPage is created (old one was disposed). Bumps starts
        // at 0 again and the render bumps it to 1.
        state.Path = "/c/x";
        var revisited = Render(view, sp);
        Assert.Equal("<span>x:1</span>", revisited);
    }

    [Fact]
    public void Router_Subroute_RendersIntoOutlet()
    {
        var routes = new[]
        {
            Route<DashboardPage>("/dashboard",
                new[] { Route<DashOverview>("overview"), Route<DashSettings>("settings/{tab}") })
        };
        var (view, state, sp) = BuildView(routes);

        state.Path = "/dashboard/overview";
        var overview = Render(view, sp);
        Assert.Equal("<div><span>dash:</span><span>overview</span></div>", overview);

        state.Path = "/dashboard/settings/billing";
        var settings = Render(view, sp);
        Assert.Equal("<div><span>dash:</span><span>settings:billing</span></div>", settings);
    }

    [Fact]
    public void Outlet_OutsideRouter_Throws()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var view = new StubComponent(() => Outlet());

        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot(sp));
    }

    [Fact]
    public void Router_NoArgs_ResolvesFromRegistry()
    {
        RouteRegistry.Reset();
        try
        {
            RouteRegistry.Add(new[]
            {
                new RouteRegistration(typeof(HomePage), "/", null),
                new RouteRegistration(typeof(UserPage), "/users/{id}", null)
            });

            var state = new RouteState();
            var services = new ServiceCollection();
            services.AddSingleton(state);
            var sp = services.BuildServiceProvider();
            var view = new StubComponent(() => Router());

            state.Path = "/";
            Assert.Equal("<span>home</span>", view.RenderAsLiveRoot(sp));

            state.Path = "/users/7";
            Assert.Equal("<span>user:7</span>", view.RenderAsLiveRoot(sp));
        }
        finally
        {
            RouteRegistry.Reset();
        }
    }

    [Fact]
    public void Router_FiresOnInitialized_OnRoutedPage()
    {
        var gate = new AsyncInitGate();
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(gate);
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => Router(new[] { Route<SyncInitPage>("/sync") }));
        state.Path = "/sync";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<span>init:1</span>", html);
    }

    [Fact]
    public async Task Router_AsyncOnInitialized_RequestsRerenderAfterCompletion()
    {
        var gate = new AsyncInitGate();
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton(gate);
        var sp = services.BuildServiceProvider();
        var handle = new RecordingRenderHandle();
        var view = new StubComponent(() => Router(new[] { Route<AsyncInitPage>("/async") })) { RenderHandle = handle };
        state.Path = "/async";

        var initial = view.RenderAsLiveRoot(sp);

        Assert.Equal("<span>loading</span>", initial);
        await gate.Started.Task;
        Assert.Equal(0, handle.RequestRenderCount);

        gate.Complete.SetResult();
        for (var i = 0; i < 50 && handle.RequestRenderCount == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.True(handle.RequestRenderCount >= 1, "expected post-await re-render on routed page");
    }

    public sealed class AsyncInitGate
    {
        public TaskCompletionSource Started { get; } = new();
        public TaskCompletionSource Complete { get; } = new();
    }

    [SkipFactory]
    public sealed class SyncInitPage : Component
    {
        public int InitCount;
        protected override void OnInitialized() => InitCount++;
        protected override Component Render() => Span(Children: [$"init:{InitCount}"]);
    }

    [SkipFactory]
    public sealed class AsyncInitPage : Component
    {
        private readonly AsyncInitGate _gate;
        public bool Loaded;
        public AsyncInitPage(AsyncInitGate gate) => _gate = gate;

        protected override async Task OnInitializedAsync()
        {
            _gate.Started.TrySetResult();
            await _gate.Complete.Task;
            Loaded = true;
        }

        protected override Component Render() => Span(Children: [Loaded ? "ready" : "loading"]);
    }

    private sealed class RecordingRenderHandle : IRenderHandle
    {
        public int RequestRenderCount;

        public Task RequestRenderAsync()
        {
            Interlocked.Increment(ref RequestRenderCount);
            return Task.CompletedTask;
        }
    }

    [SkipFactory]
    public sealed class HomePage : Component
    {
        protected override Component Render() => Span(Children: ["home"]);
    }

    [SkipFactory]
    public sealed class UserPage : Component
    {
        [RouteParam] public int Id { get; set; }
        protected override Component Render() => Span(Children: [$"user:{Id}"]);
    }

    [SkipFactory]
    public sealed class CounterPage : Component
    {
        public int Bumps { get; set; }
        [RouteParam] [QueryParam] public string? Label { get; set; }

        protected override Component Render()
        {
            Bumps++;
            return Span(Children: [$"{Label ?? "x"}:{Bumps}"]);
        }
    }

    [SkipFactory]
    public sealed class DashboardPage : Component
    {
        protected override Component Render() => Div(Children: [Span(Children: ["dash:"]), Outlet()]);
    }

    [SkipFactory]
    public sealed class DashOverview : Component
    {
        protected override Component Render() => Span(Children: ["overview"]);
    }

    [SkipFactory]
    public sealed class DashSettings : Component
    {
        [RouteParam] public string? Tab { get; set; }
        protected override Component Render() => Span(Children: [$"settings:{Tab}"]);
    }
}
