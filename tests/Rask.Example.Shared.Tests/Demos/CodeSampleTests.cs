using Rask.Core.Routing;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed partial class CodeSampleTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_EmitsTitleSourceResultNotes()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(
            () => CodeSample
                .Files(["ElementRefDemo.cs"])
                .Title("Sample title")
                .Notes("A note")
                .Result(Div["the result"]),
            TestServices.Default(js: js));

        var html = host.RenderAsLiveRoot();

        Assert.Contains("Sample title", html);
        Assert.Contains("A note", html);
        // The C# source is tokenized server-side by ColorCode, so keywords (class/public/…) are
        // wrapped in <span class="keyword"> rather than appearing as literal contiguous text.
        Assert.Contains("class=\"keyword\"", html);
        Assert.Contains("the result", html);
        Assert.Contains("sample-card", html);
    }

    [Fact]
    public void Render_NullTitleAndNotes_OmitsHeader()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(
            () => CodeSample.Files(["ElementRefDemo.js"]),
            TestServices.Default(js: js));

        var html = host.RenderAsLiveRoot();

        // Header card-header is only emitted when at least one of Title/Notes is set.
        Assert.DoesNotContain("card-header", html);
        // The real embedded JS source is shown.
        Assert.Contains("getBoundingClientRect", html);
    }

    [Fact]
    public void Render_SingleFile_ShowsFilenameLabel_NoTabStrip_HasCopyButton()
    {
        var host = new LiveHost(
            () => CodeSample.Files(["ElementRefDemo.cs"]),
            TestServices.Default(js: new FakeJsRuntime()));

        var html = host.RenderAsLiveRoot();

        // A single-file sample shows the file name as a plain label, no clickable tab strip.
        Assert.Contains("sample-code-label", html);
        Assert.Contains("ElementRefDemo.cs", html);
        Assert.DoesNotContain("sample-tabs", html);
        // The copy button is present on every card.
        Assert.Contains("sample-copy", html);
        Assert.Contains(">Copy</span>", html);
    }

    [Fact]
    public void Render_MultiFile_ShowsFilenameTabs_FirstActive_OnlyActivePane()
    {
        var host = new LiveHost(
            () => CodeSample.Files(["ElementRefDemo.cs", "ElementRefDemo.js"]),
            TestServices.Default(js: new FakeJsRuntime()));

        var html = host.RenderAsLiveRoot();

        // A tab strip with one button per file, labelled by file name, first file active.
        Assert.Contains("sample-tabs", html);
        Assert.Contains("sample-tab active", html);
        Assert.Contains(">ElementRefDemo.cs</button>", html);
        Assert.Contains(">ElementRefDemo.js</button>", html);
        // Only the active (first, C#) pane is rendered — the JS pane appears after a tab switch.
        Assert.Contains("language-csharp", html);
        Assert.DoesNotContain("language-javascript", html);
    }

    [Fact]
    public void EmbeddedSource_ReadsRealFileText()
    {
        // The real scoped JS the ElementRef sample shows must be readable from the manifest.
        var js = EmbeddedSource.Read("ElementRefDemo.js");
        Assert.Contains("getBoundingClientRect", js);
    }

    [Fact]
    public void LiveRender_ThroughApp_HighlightsServerSide_NoHljsAssets()
    {
        // Highlighting is now produced server-side by ColorCode (token <span>s in the
        // rendered HTML); there is no longer any highlight.js <link>/<script> in <head>.
        // TodosPage's CodeSample self-embeds its own source, which contains C# string
        // literals, so its tokenized output carries a <span class="string">.
        var routeState = new RouteState { Path = "/todos" };
        var html = new Shared.App().RenderAsLiveRoot(TestServices.Default(routeState: routeState));
        Assert.Contains("class=\"string\"", html);
        Assert.DoesNotContain("/lib/highlightjs/", html);
    }
}
