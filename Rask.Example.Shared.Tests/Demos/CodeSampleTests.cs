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
        Assert.Contains("var x = 1;", html);
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
    public async Task OnRenderedAsync_InvokesRaskCodeSampleRendered()
    {
        var js = new FakeJsRuntime();
        var host = new LiveHost(
            () => CodeSample(Source: "x"),
            TestServices.Default(js: js));

        host.RenderAsLiveRoot();
        await WaitFor.True(() => js.CallCount("Rask.CodeSample.rendered") > 0, TimeSpan.FromSeconds(2));

        Assert.NotEmpty(js.GetCalls("Rask.CodeSample.rendered"));
    }


    [Fact]
    public void LiveRender_ThroughApp_HljsScriptAndCssLandInHead()
    {
        // Render App so the head pipeline collects CodeSample's contributions (HomePage uses one).
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(TestServices.Default());
        Assert.Contains("/lib/highlightjs/atom-one-dark.min.css", html);
        Assert.Contains("/lib/highlightjs/highlight.min.js", html);
    }
}
