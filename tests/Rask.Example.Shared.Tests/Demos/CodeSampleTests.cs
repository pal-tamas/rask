using Rask.Core;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class CodeSampleTests
{
    [Fact]
    public void Render_EmitsTitleSourceResultNotes()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(
            () => CodeSample(
                Source: "var x = 1;",
                Title: "Sample title",
                Notes: "A note",
                Result: Div()["the result"]),
            TestServices.Default(js: js));

        var html = host.RenderAsLiveRoot();

        Assert.Contains("Sample title", html);
        Assert.Contains("A note", html);
        // The C# source is tokenized server-side by ColorCode, so `var` is wrapped in a
        // <span class="keyword"> rather than appearing as the literal contiguous string.
        Assert.Contains(">var</span>", html);
        Assert.Contains("class=\"keyword\"", html);
        Assert.Contains("the result", html);
        Assert.Contains("sample-card", html);
    }

    [Fact]
    public void Render_NullTitleAndNotes_OmitsHeader()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(
            () => CodeSample(Source: "code"),
            TestServices.Default(js: js));

        var html = host.RenderAsLiveRoot();

        // Header card-header is only emitted when at least one of Title/Notes is set.
        Assert.DoesNotContain("card-header", html);
        Assert.Contains("code", html);
    }

    [Fact]
    public void LiveRender_ThroughApp_HighlightsServerSide_NoHljsAssets()
    {
        // Highlighting is now produced server-side by ColorCode (token <span>s in the
        // rendered HTML); there is no longer any highlight.js <link>/<script> in <head>.
        // HomePage's CodeSample source contains C# string literals, so its tokenized
        // output carries a <span class="string">.
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(TestServices.Default());
        Assert.Contains("class=\"string\"", html);
        Assert.DoesNotContain("/lib/highlightjs/", html);
    }
}
