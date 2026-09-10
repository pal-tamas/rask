
namespace Rask.Core.Tests.Live;

// Regression guard for the WASM prerender hydration flash: the published site painted one completely
// unstyled frame — UA serif on a transparent ground, at 13x the document height — as the prerendered
// page handed over to the runtime.
//
// Cause: the served page carries `</head>\n<body …>`, and per the HTML parser's "after head"
// insertion mode that newline becomes a text-node child of <html>. The live <html> therefore had
// [HEAD, #text, BODY] while the runtime's full-frame payload — HtmlSerializer emits no newlines —
// parsed to [HEAD, BODY]. Neither child is keyed, so the positional walk paired #text against BODY,
// found the node names different, and REPLACED the body with a brand-new element. A freshly created
// <body> has no resolved style yet, which is the frame that was visible.
//
// The head was never the problem and was ruled out by measurement: it stayed the same element, with
// its stylesheet <link> nodes still connected.
//
// Fix: rask-morph.ts drops formatting whitespace from both sides when pairing the children of
// <html>/<head>, the containers where such text is never rendered. PrerenderShell also stops emitting
// the newline in the first place, so the published bytes and the payload agree at the source — but
// this test pins the morph, which is what holds however the shell is formatted.
//
// Written against the morph directly, in a Node subprocess with a stub DOM, for the same reason
// KeyedHeadMorphTests is: a browser assertion here would have to catch a single frame.
public sealed class PrerenderWhitespaceMorphTests
{
    [Fact]
    public void FullDocumentMorph_WithFormattingWhitespaceInHtml_KeepsTheSameBodyElement()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a failure: node is
        // not required to build or test Rask.
        var result = NodeFixture.Run("PrerenderWhitespaceMorphFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        Assert.False(root.GetProperty("threw").GetBoolean(),
            $"Full-document morph threw: {root.GetProperty("error").GetString()}");

        // The assertion that fails pre-fix: the body was replaced, so the original element is gone
        // from the tree and detached.
        Assert.True(root.GetProperty("sameBody").GetBoolean(),
            "The live <body> was replaced rather than morphed — this is the unstyled frame.");
        Assert.True(root.GetProperty("bodyStillAttached").GetBoolean(),
            "The live <body> was detached from <html>.");

        // <html> converges to the payload's shape. The formatting text node is ignored for pairing,
        // not deleted: it is not rendered, and removing it would be a change this fix does not need.
        var children = root.GetProperty("children").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "HEAD", "BODY" }, children.Where(c => c != "#text").ToArray());

        // And the morph still applied the incoming content to the body it kept.
        Assert.Equal("hello there", root.GetProperty("bodyText").GetString());
    }
}
