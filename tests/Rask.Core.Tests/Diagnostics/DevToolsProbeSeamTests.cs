using System.Text.Json;
using Rask.Core.Diagnostics;
using Rask.Core.Diagnostics.DevTools;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-built StubComponent instances have no generated factory

namespace Rask.Core.Tests.Diagnostics;

/// <summary>
///     Runs the probe tests alone. <see cref="RaskDevToolsHook.Probe" /> is process-wide, so a probe installed by one
///     test would otherwise record the renders of every other test class running beside it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DevToolsProbeCollection
{
    public const string Name = "Rask DevTools probe (process-wide)";
}

/// <summary>
///     What the render runtime reports to an attached devtools probe, observed from the outside: the seams fire, in
///     the right order, with the right component, and only for work that actually happened.
/// </summary>
[Collection(DevToolsProbeCollection.Name)]
public partial class DevToolsProbeSeamTests : global::Rask.Core.RaskMarkup, IDisposable
{
    private readonly CapturingProbe _probe = new();

    public DevToolsProbeSeamTests()
    {
        Assert.True(
            RaskDevToolsFeature.IsEnabled,
            "The Rask.DevTools.IsEnabled switch is off, so RaskDevToolsHook.Active is always null and no seam can be "
            + "observed. A Debug build of this repository sets it through Directory.Build.targets.");

        RaskDevToolsHook.Probe = _probe;
    }

    public void Dispose()
    {
        RaskDevToolsHook.Probe = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_nested_components_first_render_is_uncached_then_completed_then_walked()
    {
        var inner = new StubComponent(() => Span["x"]);
        var view = new StubComponent(() => Div[inner]);

        view.RenderAsLiveRoot();

        var forInner = _probe.Events.Where(e => ReferenceEquals(e.Component, inner)).ToList();
        string[] expected = ["rendering", "rendered", "walked"];
        Assert.Equal(expected, forInner.Select(e => e.Name));

        // A first render is a props render: mounting marks the props dirty, exactly as it does for a component a
        // chain entry built. This used to read Uncached only because `inner`, built with `new`, was never mounted
        // at all — the walk adopts such an instance now.
        Assert.Equal(RenderCause.Props, forInner[0].Cause);
        Assert.True(_probe.Commits >= 1, "a live root render commits its tree");
    }

    [Fact]
    public void StateHasChanged_is_reported_and_makes_the_next_render_a_state_render()
    {
        var inner = new StubComponent(() => Span["x"]);
        var view = new StubComponent(() => Div[inner]);
        view.RenderAsLiveRoot();
        _probe.Events.Clear();

        inner.StateHasChanged();
        view.RenderAsLiveRoot();

        Assert.Contains(_probe.Events, e => e.Name == "state-requested" && ReferenceEquals(e.Component, inner));
        Assert.Contains(
            _probe.Events,
            e => e.Name == "rendering" && ReferenceEquals(e.Component, inner) && e.Cause == RenderCause.State);
    }

    [Fact]
    public void A_clean_nested_component_is_walked_again_but_not_rendered_again()
    {
        // The cause is only worked out for a render that really ran. A component served from its cache must not
        // show up as rendering, or the devtools' render counts would count walks.
        var inner = new StubComponent(() => Span["x"]);
        var view = new StubComponent(() => Div[inner]);
        view.RenderAsLiveRoot();
        _probe.Events.Clear();

        view.RenderAsLiveRoot();

        Assert.DoesNotContain(_probe.Events, e => e.Name == "rendering" && ReferenceEquals(e.Component, inner));
        Assert.Contains(_probe.Events, e => e.Name == "walked" && ReferenceEquals(e.Component, inner));
    }

    [Fact]
    public async Task A_dispatched_handler_is_reported_with_its_owner_and_ends_without_a_fault()
    {
        var clicked = false;
        var view = new StubComponent(() => Div.OnClick(() => clicked = true));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-click")!;

        using var payload = JsonDocument.Parse("{\"type\":\"click\"}");
        Assert.True(await view.TryInvokeHandlerAsync(id, payload.RootElement));

        Assert.True(clicked);
        Assert.Collection(
            _probe.Handlers,
            start =>
            {
                Assert.Equal(id, start.HandlerId);
                Assert.Same(view, start.Owner);
                Assert.False(start.Ended);
            },
            end =>
            {
                Assert.Equal(id, end.HandlerId);
                Assert.True(end.Ended);
                Assert.Null(end.Fault);
            });
    }

    [Fact]
    public async Task Detaching_the_probe_stops_all_reporting()
    {
        RaskDevToolsHook.Probe = null;

        var view = new StubComponent(() => Div.OnClick(() => { }));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-click")!;
        using var payload = JsonDocument.Parse("{\"type\":\"click\"}");
        await view.TryInvokeHandlerAsync(id, payload.RootElement);
        view.StateHasChanged();

        Assert.Empty(_probe.Events);
        Assert.Empty(_probe.Handlers);
        Assert.Equal(0, _probe.Commits);
    }

    [Fact]
    public void A_render_that_throws_is_observed_and_still_propagates()
    {
        // ObserveThrow is an exception filter: it must see the fault and let it travel on untouched, so the boundary
        // that owns it still gets it — and a render that threw is never reported as completed.
        var inner = new StubComponent(() => throw new InvalidOperationException("boom"));
        var view = new StubComponent(() => Div[inner]);

        var thrown = Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());

        Assert.Equal("boom", thrown.Message);
        Assert.Contains(_probe.Events, e => e.Name == "threw" && ReferenceEquals(e.Component, inner));
        Assert.DoesNotContain(_probe.Events, e => e.Name == "rendered" && ReferenceEquals(e.Component, inner));
    }

    [Fact]
    public async Task A_handler_that_throws_with_no_boundary_reports_the_fault_and_rethrows()
    {
        // A method group, not a throwing lambda: `() => throw …` fits both the sync and the async handler shape.
        static void Boom() => throw new InvalidOperationException("boom");

        var view = new StubComponent(() => Div.OnClick(Boom));
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-click")!;
        using var payload = JsonDocument.Parse("{\"type\":\"click\"}");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => view.TryInvokeHandlerAsync(id, payload.RootElement).AsTask());

        var end = Assert.Single(_probe.Handlers, h => h.Ended);
        Assert.Same(thrown, end.Fault);
    }

    [Fact]
    public async Task A_handler_fault_a_boundary_catches_is_reported_with_its_owner_as_caught()
    {
        static void Boom() => throw new InvalidOperationException("boom");

        var view = new StubComponent(() => Div[ErrorBoundary[Div.OnClick(Boom)["go"]]]);
        var id = Markup.Attr(view.RenderAsLiveRoot(), "data-rask-on-click")!;
        using var payload = JsonDocument.Parse("{\"type\":\"click\"}");

        // Caught: the boundary took it, so the dispatch reports it handled rather than throwing.
        Assert.True(await view.TryInvokeHandlerAsync(id, payload.RootElement));

        var fault = Assert.Single(_probe.Faults);
        Assert.Equal("boom", fault.Exception.Message);
        Assert.Equal(ErrorSource.Action, fault.Source);
        Assert.True(fault.Caught);
        // The handler was registered inside the boundary, which is therefore its owner.
        Assert.IsType<ErrorBoundary>(fault.Component);
    }

    [Fact]
    public void A_framework_diagnostic_is_reported_to_the_probe_even_with_no_sink_listening()
    {
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = null;
        try
        {
            var error = new InvalidOperationException("boom");
            RaskDiagnostics.Report(RaskLogLevel.Warning, "Rask.Test", "a probe-seam diagnostic", error);

            var reported = Assert.Single(_probe.Diagnostics, d => d.Category == "Rask.Test");
            Assert.Equal(RaskLogLevel.Warning, reported.Level);
            Assert.Equal("a probe-seam diagnostic", reported.Message);
            Assert.Same(error, reported.Exception);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    [Fact]
    public void A_cached_component_that_changes_state_is_reported_as_a_state_render_not_an_uncached_one()
    {
        // The case the cause order exists for. A live session captures a clean pure-element subtree as frames and drops
        // the component's cached result, so an empty cache no longer means a first render.
        var inner = new StubComponent(() => Span["x"]);
        var view = new StubComponent(() => Div[inner]);
        RenderCapturingFrames(view);
        _probe.Events.Clear();

        inner.StateHasChanged();
        RenderCapturingFrames(view);

        var rendering = Assert.Single(_probe.Events, e => e.Name == "rendering" && ReferenceEquals(e.Component, inner));
        Assert.Equal(RenderCause.State, rendering.Cause);
    }

    // The page's nesting, which is what the Tree tab shows: `inner` is built outside `wrapper` and handed to it, the way
    // an indexer's children are, and it still sits INSIDE wrapper on the page. Ownership would put it beside wrapper.
    [Fact]
    public void A_walked_component_names_the_component_it_was_rendered_inside()
    {
        var inner = new StubComponent(() => Span["x"]);
        var wrapper = new StubComponent(() => Section[inner]);
        var view = new StubComponent(() => Div[wrapper]);

        view.RenderAsLiveRoot();

        Assert.Same(wrapper, Assert.Single(_probe.Walks, w => ReferenceEquals(w.Component, inner)).Parent);
        Assert.Same(view, Assert.Single(_probe.Walks, w => ReferenceEquals(w.Component, wrapper)).Parent);
    }

    [Fact]
    public void A_component_that_renders_as_its_own_element_is_walked_like_a_component_but_an_html_element_is_not()
    {
        // An External island is serialized down the element branch; it is still a component, and the devtools' tree
        // lists it. The HTML elements around it are not components and are never reported as walked.
        var island = new ElementRenderedComponent();
        var view = new StubComponent(() => Div[Section[island]]);

        view.RenderAsLiveRoot();

        var walk = Assert.Single(_probe.Walks, w => ReferenceEquals(w.Component, island));
        Assert.Same(view, walk.Parent);
        Assert.DoesNotContain(_probe.Walks, w => w.Component is Element);
    }

    private sealed class ElementRenderedComponent : Component
    {
        protected override string? TagName => "x-island";
    }

    [Fact]
    public void A_replayed_component_names_the_component_it_was_replayed_inside()
    {
        var inner = new StubComponent(() => Span["x"]);
        var wrapper = new StubComponent(() => Section[inner]);
        var view = new StubComponent(() => Div[wrapper]);
        RenderCapturingFrames(view);
        _probe.Walks.Clear();

        RenderCapturingFrames(view);

        var replay = Assert.Single(_probe.Walks, w => w.Name == "replayed" && ReferenceEquals(w.Component, inner));
        Assert.Same(wrapper, replay.Parent);
    }

    [Fact]
    public void A_clean_component_replayed_from_captured_frames_is_reported_as_replayed_not_rendered()
    {
        var inner = new StubComponent(() => Span["x"]);
        var view = new StubComponent(() => Div[inner]);
        RenderCapturingFrames(view);
        _probe.Events.Clear();

        RenderCapturingFrames(view);

        Assert.Contains(_probe.Events, e => e.Name == "replayed" && ReferenceEquals(e.Component, inner));
        Assert.DoesNotContain(_probe.Events, e => e.Name == "rendering" && ReferenceEquals(e.Component, inner));
    }

    [Fact]
    public void A_provider_is_reported_with_the_component_whose_markup_holds_it_after_its_value_is_pushed()
    {
        var theme = new SeamTheme("dark");
        var reader = new StubComponent(() => Span[Context.Get<SeamTheme>()?.Name ?? "none"]);
        var shell = new StubComponent(() => Div[Context.Provide(theme)[reader]]);
        var view = new StubComponent(() => Main[shell]);

        view.RenderAsLiveRoot();

        var provide = Assert.Single(_probe.Provides);
        Assert.Same(shell, provide.Owner);
        Assert.Same(typeof(SeamTheme), provide.Provider.ValueType);
        Assert.Same(theme, provide.Head);
    }

    [Fact]
    public void Every_kind_of_read_is_reported_with_the_component_that_read_it()
    {
        var reader = new StubComponent(() =>
        {
            _ = Context.Get<SeamTheme>();
            _ = Context.Has<int>("page-size");
            return Span[Context.Required<string>("user")];
        });
        var view = new StubComponent(() => Div[Context.Provide("ada", Name: "user")[reader]]);

        view.RenderAsLiveRoot();

        (Type, string?)[] expected = [(typeof(SeamTheme), null), (typeof(int), "page-size"), (typeof(string), "user")];
        Assert.Equal(expected, _probe.Reads.Select(r => (r.Requested, r.Name)));
        Assert.All(_probe.Reads, r => Assert.Same(reader, r.Reader));
    }

    [Fact]
    public void A_read_outside_a_live_render_is_not_reported()
    {
        _ = Context.Get<SeamTheme>();

        Assert.Empty(_probe.Reads);
    }

    private sealed record SeamTheme(string Name);

    // A live session renders with a frame writer pushed, which is what makes the serializer capture clean subtrees and
    // replay them on the next render. RenderAsLiveRoot() on its own captures nothing.
    private static void RenderCapturingFrames(Component view)
    {
        using var frames = new FrameWriter();
        var popper = FrameSinkScope.Push(frames);
        try
        {
            view.RenderAsLiveRoot();
        }
        finally
        {
            popper.Dispose();
        }
    }

    private sealed class CapturingProbe : IRaskDevToolsProbe
    {
        public List<(string Name, Component? Component, RenderCause? Cause)> Events { get; } = [];

        public List<(string HandlerId, Component Owner, Exception? Fault, bool Ended)> Handlers { get; } = [];

        public int Commits { get; private set; }

        public void WalkStarted(LiveSessionBase session, bool publishOnly) => Events.Add(("walk-started", null, null));

        public long ComponentRendering(Component component, RenderCause cause)
        {
            Events.Add(("rendering", component, cause));
            return 1;
        }

        public void ComponentRendered(Component component, long startTimestamp) =>
            Events.Add(("rendered", component, null));

        public List<(string Name, Component Component, Component? Parent)> Walks { get; } = [];

        public void ComponentWalked(Component component, Component? parent, long startTimestamp, int frameStart, int frameEnd)
        {
            Events.Add(("walked", component, null));
            Walks.Add(("walked", component, parent));
        }

        public void ComponentReplayed(Component component, Component? parent, int frameStart, int frameEnd)
        {
            Events.Add(("replayed", component, null));
            Walks.Add(("replayed", component, parent));
        }

        public bool ObserveThrow(Component component, Exception exception)
        {
            Events.Add(("threw", component, null));
            return false;
        }

        public void StateRequested(Component component) => Events.Add(("state-requested", component, null));

        public List<(Context Provider, Component Owner, object? Head)> Provides { get; } = [];

        public List<(Component Reader, Type Requested, string? Name)> Reads { get; } = [];

        public void ContextProvided(Context provider, Component owner) =>
            Provides.Add((provider, owner, ContextStack.Head?.Value));

        public void ContextRead(Component reader, Type requested, string? name) => Reads.Add((reader, requested, name));

        public List<(Component Component, Exception Exception, ErrorSource Source, bool Caught)> Faults { get; } = [];

        public List<RaskDiagnosticEvent> Diagnostics { get; } = [];

        public void ComponentFaulted(Component component, Exception exception, ErrorSource source, bool caught) =>
            Faults.Add((component, exception, source, caught));

        public void DiagnosticReported(in RaskDiagnosticEvent diagnostic) => Diagnostics.Add(diagnostic);

        public long HandlerStarting(Component owner, string handlerId, JsonElement payload)
        {
            Handlers.Add((handlerId, owner, null, false));
            return 1;
        }

        public void HandlerEnded(Component owner, string handlerId, long startTimestamp, Exception? fault) =>
            Handlers.Add((handlerId, owner, fault, true));

        public void TreeCommitted(
            Component root, IReadOnlyCollection<Component> aliveNow, IReadOnlyCollection<Component> alivePrev) =>
            Commits++;

        public void DiffComputed(LiveSessionBase session, int opCount, bool usedDiff, long startTimestamp)
        {
        }

        public void FrameSent(LiveSessionBase session, int bytes)
        {
        }

        public void FrameReceived(LiveSessionBase session, int bytes, JsonElement frame)
        {
        }
    }
}
