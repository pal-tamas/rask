using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

// WHEN an island mounts, driven against the production rask-external.js in node. `hydrate="none"` lives in
// ExternalRuntimeTests; these are the policies that do mount, and the promise they share — an island that left
// before its moment came is never mounted afterwards.
public sealed class ExternalHydrationTests
{
    private const string Fixture = "ExternalHydrationFixture";

    [Fact]
    public void An_island_with_no_policy_mounts_on_load_like_one_that_asks_for_it()
    {
        var doc = Run();

        var mounted = (doc.GetProperty("defaultMounted").GetBoolean(), doc.GetProperty("loadMounted").GetBoolean());

        Assert.Equal((true, true), mounted);
    }

    [Fact]
    public void An_idle_island_mounts_when_the_browser_goes_idle()
    {
        var doc = Run();

        var before = doc.GetProperty("idleMountedBeforeIdle").GetBoolean();
        var after = doc.GetProperty("idleMountedAfterIdle").GetBoolean();

        // Not even its chunk is fetched early: deferring the mount while paying for the download up front would
        // spend the bandwidth the policy exists to keep free.
        Assert.False(doc.GetProperty("idleRequestedBeforeIdle").GetBoolean(), "an idle island fetched its chunk before the browser was idle");
        Assert.False(before, "an idle island mounted before the browser was idle");
        Assert.True(after, "an idle island never mounted once the browser went idle");
    }

    [Fact]
    public void A_visible_island_mounts_only_once_it_scrolls_into_view()
    {
        var doc = Run();

        var offscreen = doc.GetProperty("visibleMountedOffscreen").GetBoolean();
        var inView = doc.GetProperty("visibleMountedInView").GetBoolean();

        Assert.True(doc.GetProperty("visibleObserved").GetBoolean(), "a visible island was never handed to an IntersectionObserver");
        Assert.False(doc.GetProperty("visibleRequestedOffscreen").GetBoolean(), "a visible island fetched its chunk while off screen");
        Assert.False(offscreen, "a visible island mounted on a report that it is NOT intersecting");
        Assert.True(inView, "a visible island never mounted once it intersected");
    }

    [Fact]
    public void A_visible_island_stops_watching_once_it_has_mounted()
    {
        // An observer left connected reports every later scroll in and out for the life of the page, to a callback
        // whose only job is already done.
        var doc = Run();

        var stillWatching = doc.GetProperty("visibleObserversAfterMount").GetInt32();

        Assert.Equal(0, stillWatching);
        Assert.Equal(1, doc.GetProperty("visibleMounts").GetInt32());
    }

    [Fact]
    public void An_island_removed_before_its_schedule_fires_never_mounts()
    {
        var doc = Run();

        var requested = Names(doc, "requested");
        var mounted = Names(doc, "mounted");

        // Cancelled at the source, not merely ignored when it fires: the idle callback is withdrawn and the observer
        // disconnected, so nothing is left holding the detached element.
        Assert.True(doc.GetProperty("idleCallbackCancelled").GetBoolean(), "the pending idle callback was not cancelled on unmount");
        Assert.Equal(0, doc.GetProperty("visibleGoneObservers").GetInt32());
        Assert.DoesNotContain("IdleGone", requested);
        Assert.DoesNotContain("VisibleGone", requested);
        Assert.DoesNotContain("IdleFallbackGone", requested);
        Assert.DoesNotContain("IdleGone", mounted);
        Assert.DoesNotContain("VisibleGone", mounted);
        Assert.DoesNotContain("IdleFallbackGone", mounted);
    }

    [Fact]
    public void A_browser_without_the_scheduling_apis_still_mounts_idle_and_visible_islands()
    {
        // Safari has no requestIdleCallback. An island that waited for an API that never arrives would stay server
        // markup for ever, with nothing in the console to say why.
        var doc = Run();

        var idle = doc.GetProperty("idleFallbackMounted").GetBoolean();
        var visible = doc.GetProperty("visibleFallbackMounted").GetBoolean();

        Assert.False(doc.GetProperty("idleFallbackMountedSameTurn").GetBoolean(), "the idle fallback mounted synchronously, which defers nothing");
        Assert.True(idle, "an idle island never mounted without requestIdleCallback");
        Assert.True(visible, "a visible island never mounted without IntersectionObserver");
    }

    private static string?[] Names(JsonElement doc, string property) =>
        [.. doc.GetProperty(property).EnumerateArray().Select(e => e.GetString())];

    private static JsonElement Run()
    {
        var doc = NodeFixture.Run(Fixture);
        Assert.SkipWhen(doc is null, "node is not on PATH, so the hydration policies were not exercised.");

        return doc!.Value;
    }
}
