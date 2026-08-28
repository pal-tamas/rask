using Rask.Core.Live;
using Rask.TestSupport;

namespace Rask.TypeScript.Tests;

#pragma warning disable RASK014 // the test hands the very instance it renders to the context

/// <summary>An island whose only interactive surface is a callback prop.</summary>
public sealed partial class Ticker : ReactComponent
{
    /// <summary>Runs when the ticker is clicked.</summary>
    public Action? OnTick { get; set; }
}

/// <summary>An island with no callbacks at all.</summary>
public sealed partial class Readout : ReactComponent
{
    /// <summary>The value to show.</summary>
    public int Value { get; set; }
}

// The auto render ladder serves a page as a plain document when nothing in the render needed a
// connection. That verdict is formed from what the walk REPORTED, so anything registering a handler
// without reporting is invisible to it — the page then ships with no session while still carrying
// markup that expects one.
//
// An island's callbacks ride the same handler channel as data-rask-on-click and are exactly as
// dependent on a live session: the client's hostSend() needs __raskHost, which only the Server and
// WASM runtimes publish. So they have to count.
//
// The failure this pins is silent in the worst way. Everything renders, the chunk loads, React
// mounts, the UI looks finished — and the first click reaches a page with no socket to send on.
public partial class TypeScriptInteractivityTests
{
    [Fact]
    public void A_callback_prop_makes_the_page_interactive()
    {
        var handle = new RecordingHandle();

        Render(new Ticker { OnTick = () => { } }, handle);

        Assert.True(
            handle.Reasons.HasFlag(InteractivityReason.Handler),
            "an island callback did not report a need for a live session, so the page would be served "
            + "as a static document and the callback would reach nobody");
    }

    [Fact]
    public void An_ordinary_element_handler_makes_the_page_interactive()
    {
        // The control for the test above. Without it, a rig that observed nothing at all would report
        // the same failure, and a fix that also reported nothing would look like it worked.
        var handle = new RecordingHandle();

        Render(new ClickableHost(), handle);

        Assert.True(
            handle.Reasons.HasFlag(InteractivityReason.Handler),
            "the rig cannot observe escalation at all, so the island assertion above proves nothing");
    }

    [Fact]
    public void An_island_with_no_callbacks_leaves_the_page_static()
    {
        // The other half, and the reason this cannot be fixed by reporting unconditionally from the
        // island: a presentational island is genuinely fine on a document with no session. It mounts
        // from its own script tag, and paying for a socket it never uses would put the ladder's
        // cheapest rung out of reach for anyone using islands at all.
        var handle = new RecordingHandle();

        Render(new Readout { Value = 41 }, handle);

        Assert.Equal(InteractivityReason.None, handle.Reasons);
    }

    [Fact]
    public void A_declared_but_unset_callback_leaves_the_page_static()
    {
        // A null callback writes no handler id into the props, so it must not escalate either —
        // otherwise every island declaring an optional callback would drag a socket onto pages that
        // never use one.
        var handle = new RecordingHandle();

        Render(new Ticker { OnTick = null }, handle);

        Assert.Equal(InteractivityReason.None, handle.Reasons);
    }

    private static void Render(Component host, IRenderHandle handle)
    {
        // The handle has to be on the component BEFORE Begin: LiveRenderContext reads it once, off
        // the root, in its constructor.
        host.RenderHandle = handle;

        using var scope = RenderHarness.Render(host, RenderHarness.EmptyServices());
        scope.Resolved.ToHtml();
    }

    // A plain Rask component with a wired handler — the shape whose escalation already works.
    internal sealed partial class ClickableHost : Component
    {
        protected override Component? Render() => Div.OnClick(() => { })["click"];
    }

    private sealed class RecordingHandle : IRenderHandle
    {
        public InteractivityReason Reasons { get; private set; } = InteractivityReason.None;

        public Task RequestRenderAsync() => Task.CompletedTask;

        void IRenderHandle.ReportRequiresLiveSession(InteractivityReason reason) => Reasons |= reason;
    }
}
