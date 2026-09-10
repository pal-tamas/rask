
namespace Rask.Core.Tests.Live;

// The keyed reconciliation must not relocate nodes that are already in the right order (#1049).
//
// A prerendered <head> is [shell nodes][document nodes] while the runtime's full-frame payload carries
// the document's alone. The anchor used to start on the first from-child — a SHELL node the incoming
// tree never claims — so every node the payload did claim looked out of place and was moved in front of
// it. 22 moveBefore calls on the landing page, for a head whose survivors were already in order.
//
// Moving a <link rel=stylesheet> RE-RESOLVES its sheet. Measured on the published bundle: three of the
// four stylesheets left document.styleSheets for ~37ms while their elements stayed connected, unremoved
// and un-mutated — one completely unstyled frame, UA serif on a transparent ground at 13x the height,
// on every load.
//
// Counted rather than observed, because a browser reports an atomic move as a removal plus an addition,
// exactly like a replacement: a mutation log cannot tell the two apart, and the DOM afterwards looks
// correct either way. The move count is the only thing that distinguishes them.
public sealed class KeyedAnchorSkipsDoomedTests
{
    [Fact]
    public void AHeadWhoseSurvivorsAreAlreadyInOrder_MovesNothing()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a failure: node is
        // not required to build or test Rask.
        var result = NodeFixture.Run("KeyedAnchorSkipsDoomedFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        Assert.False(root.GetProperty("threw").GetBoolean(),
            $"keyed head morph threw: {root.GetProperty("error").GetString()}");

        // The assertion that fails before the fix, with 3.
        Assert.Equal(0, root.GetProperty("moves").GetInt32());

        // And identity survives, which is what keeps a stylesheet's sheet applied.
        Assert.True(root.GetProperty("sameTitle").GetBoolean(), "the <title> was replaced");
        Assert.True(root.GetProperty("sameCss").GetBoolean(), "the stylesheet <link> was replaced");
        Assert.True(root.GetProperty("sameMeta").GetBoolean(), "the <meta> was replaced");

        // The shell's own nodes are gone, which is the reconciliation doing its actual job.
        var children = root.GetProperty("children").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "TITLE", "LINK", "META" }, children);
    }
}
