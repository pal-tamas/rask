namespace Rask.Core.Tests.Live;

// The third enforcement point of the external-component diff boundary (Rask.External). FrameDiffer skipping the
// subtree is not enough on its own: a full-document morph is a SEPARATE path to the same DOM, taken
// on scoped-CSS delivery, on reconnect, and on any structural op the diff can't trust.
//
// The failure it guards is specific and silent. What the foreign renderer built and what the server
// thinks is there permanently disagree — the children were created in the browser after mount, and
// the server's HTML either has none of them or still carries the slot templates the client lifted out
// — so the incoming side of every morph has nothing where the live DOM has a mounted component.
// Without the boundary the positional walk trims all of it, and the component goes blank on the next
// full reply.
//
// Runs the production rask-morph.ts under node; see OpaqueMorphFixture.ts.
public sealed class OpaqueMorphTests
{
    [Fact]
    public void Morph_OpaqueHost_KeepsForeignChildrenAndStillUpdatesProps()
    {
        var doc = NodeFixture.Run("OpaqueMorphFixture");
        if (doc is null)
        {
            // No node on PATH. The browser E2E covers the user-observable half.
            return;
        }

        var opaque = doc.Value.GetProperty("opaque");

        Assert.False(opaque.GetProperty("threw").GetBoolean(),
            $"morph threw: {opaque.GetProperty("error").GetString()}");

        // The component's own subtree survives untouched — those nodes belong to its renderer.
        var survivors = opaque.GetProperty("survivingChildren")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "DIV" }, survivors);

        // ...while props still cross the boundary, which is how a changed prop reaches the adapter.
        Assert.Equal("""{"total":2}""", opaque.GetProperty("props").GetString());
    }

    [Fact]
    public void Morph_WithoutTheMarker_RemovesTheChildren()
    {
        // The negative control, and the reason the test above is worth having. Identical shapes with
        // the marker off: the morph really does trim the whole subtree. Without this, a boundary that
        // silently stopped working would still look green.
        var doc = NodeFixture.Run("OpaqueMorphFixture");
        if (doc is null)
        {
            return;
        }

        var transparent = doc.Value.GetProperty("transparent");

        Assert.False(transparent.GetProperty("threw").GetBoolean(),
            $"morph threw: {transparent.GetProperty("error").GetString()}");
        Assert.Empty(transparent.GetProperty("survivingChildren").EnumerateArray());
    }
}
