using System.Text.Json;
using Rask.TestSupport;

namespace Rask.External.Tests;

// The browser half of the boundary, driven against the production rask-external.js in node.
//
// Each of these guards a failure that is invisible from the C# side: the server render is identical
// whether or not the runtime remounts on every prop change, loses callback identity, or fetches a
// chunk it was told never to load.
public sealed class ExternalRuntimeTests
{

    [Fact]
    public void A_prop_change_updates_the_island_rather_than_remounting_it()
    {
        // The single most important behaviour here. A remount would throw away every bit of state the
        // component owns — scroll position, focus, an open dropdown, a half-typed field — on a prop
        // change the user never asked to be destructive.
        var doc = NodeFixture.Run("ExternalRuntimeFixture");
        if (doc is null)
        {
            return;
        }

        var log = doc.Value.GetProperty("log").EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(new[] { "mount", "update", "unmount" }, log);
        Assert.Equal("Costs", doc.Value.GetProperty("headingAfterUpdate").GetString());
    }

    [Fact]
    public void A_callback_keeps_its_identity_across_updates()
    {
        // React compares props by identity, so a fresh closure per update invalidates every
        // useCallback and memo keyed on the callback and re-fires every useEffect listing it. The
        // symptom is a component that appears to work while re-subscribing on every render.
        var doc = NodeFixture.Run("ExternalRuntimeFixture");
        if (doc is null)
        {
            return;
        }

        Assert.True(doc.Value.GetProperty("callbackIsFunction").GetBoolean(),
            "the $h handler reference was not revived into a function");
        Assert.True(doc.Value.GetProperty("callbackIdentityStable").GetBoolean(),
            "the same handler id produced two different function objects");
    }

    [Fact]
    public void Calling_a_callback_reaches_the_host_dispatch_channel()
    {
        var doc = NodeFixture.Run("ExternalRuntimeFixture");
        if (doc is null)
        {
            return;
        }

        var dispatched = Assert.Single(doc.Value.GetProperty("dispatched").EnumerateArray());

        // The same channel every DOM handler uses — the open socket on the Server, a direct JSExport
        // call on WASM — rather than one the island opened for itself.
        Assert.Equal("c7:3", dispatched.GetProperty("id").GetString());
        Assert.Equal("external", dispatched.GetProperty("type").GetString());
        Assert.Equal(42, dispatched.GetProperty("args")[0].GetInt32());
    }

    [Fact]
    public void Hydration_none_never_requests_the_chunk()
    {
        // "Ships no JavaScript" has to mean the bytes are never fetched, not merely that mount is
        // skipped after they arrive.
        var doc = NodeFixture.Run("ExternalRuntimeFixture");
        if (doc is null)
        {
            return;
        }

        var requested = doc.Value.GetProperty("requested")
            .EnumerateArray().Select(e => e.GetString()).ToArray();

        Assert.Equal(new[] { "Chart" }, requested);
        Assert.DoesNotContain("Inert", requested);
    }
}
