using System.Text.Json;
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
        Assert.Equal(RenderCause.Uncached, forInner[0].Cause);
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

        public void ComponentWalked(Component component, long startTimestamp, int frameStart, int frameEnd) =>
            Events.Add(("walked", component, null));

        public void ComponentReplayed(Component component, int frameStart, int frameEnd) =>
            Events.Add(("replayed", component, null));

        public bool ObserveThrow(Component component, Exception exception)
        {
            Events.Add(("threw", component, null));
            return false;
        }

        public void StateRequested(Component component) => Events.Add(("state-requested", component, null));

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
