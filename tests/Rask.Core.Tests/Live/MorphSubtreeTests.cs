
namespace Rask.Core.Tests.Live;

// Client-behaviour guard for the MorphSubtree diff op (rask-dom.ts applyDiff `case 8` →
// rask-morph.ts `morph`). MorphSubtree is the Raw-tainted fallback shrunk from a
// full-document morph to one parent's children: the server emits it (FrameDifferTests
// pins the op) and the client morphs just that subtree. This exercises the production
// applyDiff + morph in a Node subprocess and asserts the scoped morph converges without
// touching a focused node outside the morphed parent. Real-browser coverage of the same
// op rides the Playwright E2E guide journeys.
public sealed class MorphSubtreeTests
{
    [Fact]
    public void MorphSubtree_ReconcilesTaintedSubtree_WithoutDisturbingOutsideFocus()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a
        // failure: node is not required to build or test Rask, and the browser-observable
        // half of this behaviour is covered by an E2E test.
        var result = NodeFixture.Run("MorphSubtreeFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        Assert.False(root.GetProperty("threw").GetBoolean(),
            $"applyDiff threw: {root.GetProperty("error").GetString()}");
        // The op kind must be recognised — a fall-through to the default branch reloads the page.
        Assert.False(root.GetProperty("reloaded").GetBoolean(), "MorphSubtree must not trigger a full reload");

        // The Raw-expanded run reconciled: the <b> was dropped (node-count change) and the sibling
        // <span> text flipped x → y — exactly what a full-document morph would have done, scoped.
        var children = root.GetProperty("children").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "A", "SPAN" }, children);
        Assert.Equal("y", root.GetProperty("spanText").GetString());

        // The focused <input> OUTSIDE the morphed parent kept focus and its place — the morph is
        // scoped to the tainted subtree, never the whole document.
        Assert.True(root.GetProperty("focusKept").GetBoolean(),
            "a focused node outside the morphed parent must keep focus");
    }


}
