using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class CodeSampleTests
{
    [Fact]
    public void Render_EmitsTitleSourceResultNotes()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(
            () => CodeSample(
                "var x = 1;",
                "Sample title",
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
            () => CodeSample("code"),
            TestServices.Default(js: js));

        var html = host.RenderAsLiveRoot();

        // Header card-header is only emitted when at least one of Title/Notes is set.
        Assert.DoesNotContain("card-header", html);
        Assert.Contains("code", html);
    }

    [Fact]
    public void Render_SingleLanguage_KeepsCSharpLabel_NoTabStrip_HasCopyButton()
    {
        var host = new LiveHost(
            () => CodeSample("var x = 1;"),
            TestServices.Default(js: new FakeJsRuntime()));

        var html = host.RenderAsLiveRoot();

        // A C#-only sample keeps the plain label and shows no tab strip (back-compat).
        Assert.Contains("sample-code-label", html);
        Assert.DoesNotContain("sample-tabs", html);
        // The copy button is present on every card.
        Assert.Contains("sample-copy", html);
        Assert.Contains(">Copy</span>", html);
    }

    [Fact]
    public void Render_MultiLanguage_ShowsTabsWithCSharpActive_AndOnlyActivePane()
    {
        var host = new LiveHost(
            () => CodeSample(
                "var x = 1;",
                Js: "export function f() { return 1; }",
                Css: ".a { color: red; }"),
            TestServices.Default(js: new FakeJsRuntime()));

        var html = host.RenderAsLiveRoot();

        // A tab strip with one button per supplied language, C# active by default.
        Assert.Contains("sample-tabs", html);
        Assert.Contains("sample-tab active", html);
        Assert.Contains(">C#</button>", html);
        Assert.Contains(">JS</button>", html);
        Assert.Contains(">CSS</button>", html);
        // Only the active (C#) pane is rendered — the JS/CSS panes appear after a tab switch.
        Assert.Contains("language-csharp", html);
        Assert.DoesNotContain("language-javascript", html);
        Assert.DoesNotContain("language-css", html);
    }

    [Fact]
    public void EmbeddedSource_ReadsRealFileText_AndJoinsMultiple()
    {
        // The real scoped JS the ElementRef sample shows must be readable from the manifest.
        var js = EmbeddedSource.Read("ElementRefDemo.js");
        Assert.Contains("getBoundingClientRect", js);

        // Multiple files join into one pane (the scoped-CSS sample shows both stylesheets).
        var css = EmbeddedSource.Read("ScopedRed.css", "ScopedBlue.css");
        Assert.Contains("#d23030", css); // red dot
        Assert.Contains("#0066B3", css); // blue dot
    }

    [Fact]
    public void LiveRender_ThroughApp_HighlightsServerSide_NoHljsAssets()
    {
        // Highlighting is now produced server-side by ColorCode (token <span>s in the
        // rendered HTML); there is no longer any highlight.js <link>/<script> in <head>.
        // HomePage's CodeSample source contains C# string literals, so its tokenized
        // output carries a <span class="string">.
        var html = new Shared.App().RenderAsLiveRoot(TestServices.Default());
        Assert.Contains("class=\"string\"", html);
        Assert.DoesNotContain("/lib/highlightjs/", html);
    }
}
