using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Routing;

// A page declares its title and the layout above it reads it through RouteState — in the SAME render call,
// which is the whole point (#1239): the first HTML carries the crumb, and nothing flashes empty.
//
// The page is mounted where its layout places the Outlet, as it always was, so the layout has rendered by
// the time the title is known. When the title CHANGED, the root walks once more inside the same call — one
// more render of whatever read it — and when it did not, nothing renders again.
public partial class PageTitleTests : global::Rask.Core.RaskMarkup
{
    private static readonly List<string> Log = [];

    private static (StubComponent view, RouteState state, IServiceProvider sp) BuildView(IReadOnlyList<Route> routes)
    {
        Log.Clear();
        TitleLayout.Renders = 0;
        var state = new RouteState();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => Router.Routes(routes));
        return (view, state, sp);
    }

    private static Route[] Under<TLayout>(params Route[] children) where TLayout : Component =>
        [Route.To<TLayout>("/", children)];

    [Fact]
    public void A_static_title_reaches_the_layout_in_the_first_render()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<StaticPage>("static")));
        state.Path = "/static";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>Static</nav><span>static</span></div>", html);
    }

    [Fact]
    public void A_title_that_changed_costs_the_layout_one_more_render_and_the_page_none()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<CountingPage>("counted")));
        CountingPage.Mounts = 0;
        CountingPage.Renders = 0;
        CountingPage.Rendered = 0;
        state.Path = "/counted";

        view.RenderAsLiveRoot(sp);

        Assert.Equal(2, TitleLayout.Renders);
        Assert.Equal(1, CountingPage.Mounts);
        Assert.Equal(1, CountingPage.Renders);
        Assert.Equal(1, CountingPage.Rendered);
    }

    [Fact]
    public void A_title_built_from_a_route_parameter_follows_the_url_on_the_same_page_instance()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<ParamPage>("edit/{name}")));
        state.Path = "/edit/one";
        var first = view.RenderAsLiveRoot(sp);

        state.Path = "/edit/two";
        var second = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>Edit one</nav><span>one#1</span></div>", first);
        Assert.Equal("<div><nav>Edit two</nav><span>two#1</span></div>", second);
    }

    [Fact]
    public void A_page_without_a_title_leaves_the_layout_without_one()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<UntitledPage>("plain")));
        state.Path = "/plain";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>-</nav><span>plain</span></div>", html);
        Assert.Null(state.Title);
    }

    [Fact]
    public void Leaving_a_titled_page_for_an_untitled_one_clears_the_title()
    {
        var (view, state, sp) = BuildView(
            Under<TitleLayout>(Route.To<StaticPage>("static"), Route.To<UntitledPage>("plain")));
        state.Path = "/static";
        view.RenderAsLiveRoot(sp);

        state.Path = "/plain";
        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>-</nav><span>plain</span></div>", html);
    }

    [Fact]
    public void An_unmatched_path_clears_the_title()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<StaticPage>("static")));
        state.Path = "/static";
        view.RenderAsLiveRoot(sp);

        state.Path = "/nowhere/at/all";
        view.RenderAsLiveRoot(sp);

        Assert.Null(state.Title);
    }

    [Fact]
    public void A_title_from_data_loaded_before_the_first_await_is_in_the_first_render()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<SyncLoadedPage>("sync/{id}")));
        state.Path = "/sync/7";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>Record 7</nav><span>record 7</span></div>", html);
    }

    [Fact]
    public async Task A_title_from_data_loaded_after_an_await_settles_into_the_served_html()
    {
        QuiescenceScope.ResetSyncForTests();
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<AsyncLoadedPage>("async/{id}")));
        state.Path = "/async/7";

        var result = await QuiescentRender.Run(
            publishOnly => view.RenderAsLiveRoot(sp, publishOnly),
            TimeSpan.FromSeconds(10), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.TimedOut);
        Assert.Equal("<div><nav>Record 7</nav><span>record 7</span></div>", result.Html);
    }

    [Fact]
    public void A_rename_without_navigation_renders_the_layout_exactly_once_more()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<RenamablePage>("rename")));
        state.Path = "/rename";
        view.RenderAsLiveRoot(sp);
        var before = TitleLayout.Renders;

        RenamablePage.Captured!.Rename("Renamed");
        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>Renamed</nav><span>Renamed</span></div>", html);
        Assert.Equal(before + 1, TitleLayout.Renders);
    }

    [Fact]
    public void A_page_that_rerenders_with_the_same_title_does_not_render_the_layout_again()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<RenamablePage>("rename")));
        state.Path = "/rename";
        view.RenderAsLiveRoot(sp);
        var before = TitleLayout.Renders;

        RenamablePage.Captured!.Rename("Original");
        view.RenderAsLiveRoot(sp);
        view.RenderAsLiveRoot(sp);

        Assert.Equal(before, TitleLayout.Renders);
    }

    [Fact]
    public void The_leaf_title_wins_over_the_title_of_a_layout_between_it_and_the_root()
    {
        var routes = Under<TitleLayout>(
            Route.To<TitledSection>("section", [Route.To<StaticPage>("static")]));
        var (view, state, sp) = BuildView(routes);
        state.Path = "/section/static";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>Static</nav><section><span>static</span></section></div>", html);
    }

    [Fact]
    public void A_layouts_own_title_stands_in_for_a_leaf_that_declares_none()
    {
        var routes = Under<TitleLayout>(
            Route.To<TitledSection>("section", [Route.To<UntitledPage>("plain")]));
        var (view, state, sp) = BuildView(routes);
        state.Path = "/section/plain";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>Section</nav><section><span>plain</span></section></div>", html);
    }

    [Fact]
    public void A_component_in_the_layouts_header_follows_the_title_while_the_layout_stays_cached()
    {
        var (view, state, sp) = BuildView(Under<CrumbHostLayout>(Route.To<RenamablePage>("rename")));
        state.Path = "/rename";
        var first = view.RenderAsLiveRoot(sp);
        var layoutRenders = CrumbHostLayout.Renders;

        RenamablePage.Captured!.Rename("Renamed");
        var second = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><b>Original</b><span>Original</span></div>", first);
        Assert.Equal("<div><b>Renamed</b><span>Renamed</span></div>", second);
        Assert.Equal(layoutRenders, CrumbHostLayout.Renders);
    }

    [Fact]
    public void A_reader_above_the_router_sees_the_title_in_the_same_render()
    {
        var routes = new[] { Route.To<CountingPage>("/counted") };
        var (_, state, sp) = BuildView(routes);
        CountingPage.Mounts = 0;
        CountingPage.Rendered = 0;
        var view = new StubComponent(() => Div[P[state.Title ?? "-"], Router.Routes(routes)]);
        state.Path = "/counted";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><p>Counted</p><span>counted</span></div>", html);
        Assert.Equal(1, CountingPage.Mounts);
        Assert.Equal(1, CountingPage.Rendered);
    }

    [Fact]
    public void A_page_is_mounted_where_its_layout_places_the_outlet_after_the_layout_has_rendered()
    {
        var (view, state, sp) = BuildView(Under<LoggingLayout>(Route.To<LoggingPage>("logged")));
        state.Path = "/logged";

        view.RenderAsLiveRoot(sp);

        Assert.Equal(
            [
                "layout.OnMount", "layout.OnUpdated", "layout.Render",
                "page.OnMount", "page.OnUpdated", "page.Render",
                "layout.OnFirstRender", "layout.OnRendered", "page.OnFirstRender", "page.OnRendered",
            ],
            Log);
    }

    [Fact]
    public void Navigating_away_unmounts_the_page_before_its_layout()
    {
        var routes = new[]
        {
            Route.To<LoggingLayout>("/in", [Route.To<LoggingPage>("logged")]),
            Route.To<UntitledPage>("/out"),
        };
        var (view, state, sp) = BuildView(routes);
        state.Path = "/in/logged";
        view.RenderAsLiveRoot(sp);
        Log.Clear();

        state.Path = "/out";
        view.RenderAsLiveRoot(sp);

        Assert.Equal(["page.OnUnmount", "layout.OnUnmount"], Log);
    }

    [Fact]
    public void Changing_only_the_leaf_keeps_the_layout_mounted_and_unmounts_the_old_leaf_once()
    {
        var routes = Under<LoggingLayout>(Route.To<LoggingPage>("logged"), Route.To<UntitledPage>("plain"));
        var (view, state, sp) = BuildView(routes);
        state.Path = "/logged";
        view.RenderAsLiveRoot(sp);
        Log.Clear();

        state.Path = "/plain";
        view.RenderAsLiveRoot(sp);
        view.RenderAsLiveRoot(sp);

        Assert.Equal(["page.OnUnmount"], Log.Where(e => e.EndsWith("OnUnmount", StringComparison.Ordinal)));
        Assert.DoesNotContain("layout.OnMount", Log);
        Assert.Single(Log, "layout.Render");
    }

    [Fact]
    public void A_page_whose_layout_does_not_place_the_outlet_is_neither_constructed_nor_mounted()
    {
        // The security half of the render order: a layout that withholds its Outlet — behind an Authorize,
        // behind a condition — keeps the page's constructor and its hooks from running at all.
        var (view, state, sp) = BuildView(Under<GatedLayout>(Route.To<CountingPage>("counted")));
        CountingPage.Constructed = 0;
        CountingPage.Mounts = 0;
        GatedLayout.Open = false;
        state.Path = "/counted";

        var closed = view.RenderAsLiveRoot(sp);
        view.RenderAsLiveRoot(sp);

        Assert.Equal("<div>closed</div>", closed);
        Assert.Equal(0, CountingPage.Constructed);
        Assert.Equal(0, CountingPage.Mounts);
        Assert.Null(state.Title);
    }

    [Fact]
    public void A_page_behind_an_outlet_its_layout_opens_later_mounts_then_and_names_the_route()
    {
        var (view, state, sp) = BuildView(Under<GatedLayout>(Route.To<CountingPage>("counted")));
        CountingPage.Constructed = 0;
        CountingPage.Mounts = 0;
        GatedLayout.Open = false;
        state.Path = "/counted";
        view.RenderAsLiveRoot(sp);

        GatedLayout.Open = true;
        GatedLayout.Captured!.StateHasChanged();
        var open = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><span>counted</span></div>", open);
        Assert.Equal(1, CountingPage.Constructed);
        Assert.Equal(1, CountingPage.Mounts);
        Assert.Equal("Counted", state.Title);
    }

    [Fact]
    public void A_crumb_that_appears_ahead_of_the_outlet_keeps_the_page_it_is_named_after()
    {
        // The layout renders one sibling more ahead of its Outlet once the page has a title, which gives it
        // a NEW outlet. A page that went with its outlet would be constructed again, lose what it loaded,
        // lose its title, and take the crumb away with it.
        var (view, state, sp) = BuildView(Under<CrumbAheadLayout>(Route.To<CountingPage>("counted")));
        CountingPage.Constructed = 0;
        CountingPage.Mounts = 0;
        state.Path = "/counted";

        var html = view.RenderAsLiveRoot(sp);
        var again = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><b>Counted</b><span>counted</span></div>", html);
        Assert.Equal(html, again);
        Assert.Equal(1, CountingPage.Constructed);
        Assert.Equal(1, CountingPage.Mounts);
    }

    [Fact]
    public void A_value_the_layout_provides_is_in_scope_in_the_pages_OnMount()
    {
        var (view, state, sp) = BuildView(Under<ProvidingLayout>(Route.To<ContextReadingPage>("reads")));
        state.Path = "/reads";

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><span>mounted with from-layout</span></div>", html);
    }

    [Fact]
    public void An_error_boundary_around_the_outlet_catches_a_route_value_that_will_not_bind()
    {
        var (view, state, sp) = BuildView(Under<GuardingLayout>(Route.To<NumberedPage>("n/{id}")));
        state.Path = "/n/not-a-number";

        var html = view.RenderAsLiveRoot(sp);

        Assert.StartsWith("<div>layout:<p>caught RouteBindException</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_boundary_around_the_outlet_catches_a_page_whose_constructor_throws()
    {
        var (view, state, sp) = BuildView(Under<GuardingLayout>(Route.To<UnconstructiblePage>("broken")));
        state.Path = "/broken";

        var html = view.RenderAsLiveRoot(sp);

        Assert.StartsWith("<div>layout:<p>caught InvalidOperationException</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_router_nested_inside_a_page_does_not_take_the_title_from_the_outer_one()
    {
        NestedRouterPage.Inner = [Route.To<StaticPage>("/outer")];
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<NestedRouterPage>("outer")));
        state.Path = "/outer";

        var html = view.RenderAsLiveRoot(sp);
        view.RenderAsLiveRoot(sp);

        Assert.Equal("<div><nav>Outer</nav><i><span>static</span></i></div>", html);
        Assert.Equal("Outer", state.Title);
        Assert.Equal(2, TitleLayout.Renders);
    }

    [Fact]
    public void Reading_the_title_outside_a_render_returns_the_last_published_one()
    {
        var (view, state, sp) = BuildView(Under<TitleLayout>(Route.To<StaticPage>("static")));
        state.Path = "/static";
        view.RenderAsLiveRoot(sp);

        var title = state.Title;

        Assert.Equal("Static", title);
    }

    [SkipFactory]
    public sealed class TitleLayout(RouteState route) : Component
    {
        public static int Renders;

        protected override Component? Render()
        {
            Renders++;
            return Div[Nav[route.Title ?? "-"], Outlet];
        }
    }

    [SkipFactory]
    public sealed class StaticPage : Component
    {
        protected override string? PageTitle => "Static";
        protected override Component? Render() => Span["static"];
    }

    [SkipFactory]
    public sealed class UntitledPage : Component
    {
        protected override Component? Render() => Span["plain"];
    }

    [SkipFactory]
    public sealed class ParamPage : Component
    {
        private int _mounts;

        [RouteParam] public string? Name { get; set; }

        protected override string? PageTitle => $"Edit {Name}";

        protected override Task OnMount()
        {
            _mounts++;
            return Task.CompletedTask;
        }

        protected override Component? Render() => Span[$"{Name}#{_mounts}"];
    }

    [SkipFactory]
    public sealed class SyncLoadedPage : Component
    {
        private string? _record;

        [RouteParam] public int Id { get; set; }

        protected override string? PageTitle => _record is { } r ? $"Record {r}" : null;

        protected override Task OnUpdated()
        {
            _record = Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        }

        protected override Component? Render() => Span[$"record {_record}"];
    }

    [SkipFactory]
    public sealed class AsyncLoadedPage : Component
    {
        private string? _record;

        [RouteParam] public int Id { get; set; }

        protected override string? PageTitle => _record is { } r ? $"Record {r}" : null;

        protected override async Task OnUpdated()
        {
            await Task.Delay(30);
            _record = Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        protected override Component? Render() => Span[_record is null ? "loading" : $"record {_record}"];
    }

    [SkipFactory]
    public sealed class RenamablePage : Component
    {
        public static RenamablePage? Captured;
        private string _name = "Original";

        public RenamablePage() => Captured = this;

        protected override string? PageTitle => _name;

        public void Rename(string name)
        {
            _name = name;
            StateHasChanged();
        }

        protected override Component? Render() => Span[_name];
    }

    [SkipFactory]
    public sealed class TitledSection : Component
    {
        protected override string? PageTitle => "Section";

        protected override Component? Render() => global::RaskEntriesRask_Core.Section[Outlet];
    }

    [SkipFactory]
    public sealed class Crumb(RouteState route) : Component
    {
        protected override Component? Render() => B[route.Title ?? "-"];
    }

    [SkipFactory]
    public sealed class CrumbHostLayout(RouteState route) : Component
    {
        public static int Renders;
        private readonly Crumb _crumb = new(route);

        protected override Component? Render()
        {
            Renders++;
            return Div[_crumb, Outlet];
        }
    }

    [SkipFactory]
    public sealed class CountingPage : Component
    {
        public static int Constructed;
        public static int Mounts;
        public static int Renders;
        public static int Rendered;

        public CountingPage() => Constructed++;

        protected override string? PageTitle => "Counted";

        protected override Task OnMount()
        {
            Mounts++;
            return Task.CompletedTask;
        }

        protected override Task OnRendered()
        {
            Rendered++;
            return Task.CompletedTask;
        }

        protected override Component? Render()
        {
            Renders++;
            return Span["counted"];
        }
    }

    [SkipFactory]
    public sealed class CrumbAheadLayout(RouteState route) : Component
    {
        protected override Component? Render() => Div[route.Title is { } title ? B[title] : null, Outlet];
    }

    [SkipFactory]
    public sealed class ProvidingLayout : Component
    {
        protected override Component? Render() => Context.Provide("from-layout")[Div[Outlet]];
    }

    [SkipFactory]
    public sealed class ContextReadingPage : Component
    {
        private string? _seen;

        protected override Task OnMount()
        {
            _seen = Context.Get<string>();
            return Task.CompletedTask;
        }

        protected override Component? Render() => Span[$"mounted with {_seen}"];
    }

    [SkipFactory]
    public sealed class GuardingLayout : Component
    {
        protected override Component? Render() =>
            Div["layout:", ErrorBoundary.Fallback((ex, _) => P[$"caught {ex.GetType().Name}"])[Outlet]];
    }

    [SkipFactory]
    public sealed class NumberedPage : Component
    {
        [RouteParam] public int Id { get; set; }

        protected override Component? Render() => Span[Id];
    }

    [SkipFactory]
    public sealed class UnconstructiblePage : Component
    {
        public UnconstructiblePage() => throw new InvalidOperationException("no such page");

        protected override Component? Render() => Span["never"];
    }

    [SkipFactory]
    public sealed class GatedLayout : Component
    {
        public static bool Open;
        public static GatedLayout? Captured;

        public GatedLayout() => Captured = this;

        protected override Component? Render() => Open ? Div[Outlet] : Div["closed"];
    }

    [SkipFactory]
    public sealed class NestedRouterPage : Component
    {
        public static IReadOnlyList<Route> Inner = [];

        protected override string? PageTitle => "Outer";

        protected override Component? Render() => I[Router.Routes(Inner)];
    }

    [SkipFactory]
    public sealed class LoggingLayout : Component
    {
        protected override Task OnMount() => Note("layout.OnMount");
        protected override Task OnUpdated() => Note("layout.OnUpdated");
        protected override Task OnFirstRender() => Note("layout.OnFirstRender");
        protected override Task OnRendered() => Note("layout.OnRendered");
        protected override Task OnUnmount() => Note("layout.OnUnmount");

        protected override Component? Render()
        {
            Log.Add("layout.Render");
            return Div[Outlet];
        }
    }

    [SkipFactory]
    public sealed class LoggingPage : Component
    {
        protected override Task OnMount() => Note("page.OnMount");
        protected override Task OnUpdated() => Note("page.OnUpdated");
        protected override Task OnFirstRender() => Note("page.OnFirstRender");
        protected override Task OnRendered() => Note("page.OnRendered");
        protected override Task OnUnmount() => Note("page.OnUnmount");

        protected override Component? Render()
        {
            Log.Add("page.Render");
            return Span["logged"];
        }
    }

    private static Task Note(string entry)
    {
        Log.Add(entry);
        return Task.CompletedTask;
    }
}
