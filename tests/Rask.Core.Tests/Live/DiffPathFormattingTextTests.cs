
namespace Rask.Core.Tests.Live;

// Regression guard for #1097: on a WASM page served with a newline between `</head>` and `<body>`,
// every in-place update was dropped — the click reached .NET, the diff frame arrived, and the page
// never changed, with nothing in the console.
//
// Cause: c3ddc923 made the morph ignore that newline (a text node the parser puts in <html>) when it
// pairs <html>'s children, which is what lets the node survive hydration. The diff codec's path walk
// still counted it, so a path addressing BODY as slot 1 resolved to the newline and the op was lost.
// The shipped `wasm` template's shell has exactly that newline.
//
// Fix: rask-dom.ts's path walk skips the same nodes the morph does — formatting whitespace inside
// <html>/<head>, and browser-added `data-rask-managed` nodes.
//
// Written against applyDiff directly, in a Node subprocess with a stub DOM, like
// PrerenderWhitespaceMorphTests: the browser-visible symptom is an absence, which is a poor thing to
// wait for.
public sealed class DiffPathFormattingTextTests
{
    [Fact]
    public void An_UpdateText_through_a_formatting_newline_in_the_html_reaches_its_text_node()
    {
        // No node on PATH — the JS-driven reproduction cannot run. Deliberately not a failure: node is
        // not required to build or test Rask.
        var result = NodeFixture.Run("DiffPathFormattingTextFixture");
        if (result is null)
        {
            return;
        }

        var html = result.Value.GetProperty("html");
        Assert.False(html.GetProperty("threw").GetBoolean(), html.GetProperty("error").GetString());
        Assert.False(result.Value.GetProperty("reloaded").GetBoolean(), "applyDiff fell back to a reload.");

        // The assertion that fails pre-fix: the path landed on the newline and the op was dropped.
        Assert.Equal("clicks=1", html.GetProperty("text").GetString());
    }

    [Fact]
    public void An_UpdateText_under_body_still_counts_whitespace_text_nodes()
    {
        var result = NodeFixture.Run("DiffPathFormattingTextFixture");
        if (result is null)
        {
            return;
        }

        // Whitespace between inline content under <body> is rendered and is in the frame walk, so it
        // must keep its slot. A filter that leaked past <html>/<head> would resolve slot 1 to <main>'s
        // neighbour and miss here.
        var body = result.Value.GetProperty("body");
        Assert.False(body.GetProperty("threw").GetBoolean(), body.GetProperty("error").GetString());
        Assert.Equal("v2", body.GetProperty("text").GetString());
    }
}
