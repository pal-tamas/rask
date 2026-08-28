namespace Rask.Core.Tests.Live;

// The third enforcement point of the island diff boundary (Rask.External). FrameDiffer skipping the
// subtree is not enough on its own: a full-document morph is a SEPARATE path to the same DOM, taken
// on scoped-CSS delivery, on reconnect, and on any structural op the diff can't trust.
//
// The failure it guards is specific and silent. The server renders an island as an EMPTY element —
// its children are built in the browser by React/Lit/Blazor after mount — so the incoming side of
// every morph has nothing where the live DOM has a mounted component. Without the boundary the
// positional walk trims all of it, and the island goes blank on the next full reply.
//
// Runs the production rask-morph.js under node; see OpaqueMorphFixture.mjs.
public sealed class OpaqueMorphTests
{
    [Fact]
    public void Morph_OpaqueHost_KeepsForeignChildrenAndStillUpdatesProps()
    {
        using var doc = NodeFixture.Run("tests/Rask.Core.Tests/Live/OpaqueMorphFixture.mjs", "src/Rask.Core/Resources/rask-morph.js");
        if (doc is null)
        {
            return;
        }

        var opaque = doc.RootElement.GetProperty("opaque");

        Assert.False(opaque.GetProperty("threw").GetBoolean(),
            $"morph threw: {opaque.GetProperty("error").GetString()}");

        // The island's own subtree survives untouched — those nodes belong to its renderer.
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
        using var doc = NodeFixture.Run("tests/Rask.Core.Tests/Live/OpaqueMorphFixture.mjs", "src/Rask.Core/Resources/rask-morph.js");
        if (doc is null)
        {
            return;
        }

        var transparent = doc.RootElement.GetProperty("transparent");

        Assert.False(transparent.GetProperty("threw").GetBoolean(),
            $"morph threw: {transparent.GetProperty("error").GetString()}");
        Assert.Empty(transparent.GetProperty("survivingChildren").EnumerateArray());
    }
}
