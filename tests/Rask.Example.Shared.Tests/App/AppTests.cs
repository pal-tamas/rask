using Rask.Core.Routing;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.App;

public sealed class AppTests
{
    [Fact]
    public void LiveRender_StartsWithDoctype_AndHtmlEnLang()
    {
        var html = RaskTest.RenderDocument(new Shared.App(), TestServices.Default()).Html;

        Assert.StartsWith("<!DOCTYPE html>", html);
        // data-rask-ui turns the kit's theme on for the document; the kit scopes daisyUI's palette to
        // it so that referencing the package cannot repaint an app that only wanted a button.
        Assert.Contains("<html lang=\"en\" data-rask-ui=\"\">", html);
        Assert.Contains("<body class=\"bg-ui-well\">", html);
    }

    [Fact]
    public void LiveRender_EmitsStylesheetAndMeta_InHead()
    {
        var html = RaskTest.RenderDocument(new Shared.App(), TestServices.Default()).Html;

        // Title body content is HTML-encoded: literal "—" → "&#x2014;". GuidesIndexPage
        // (the site root) overrides App's fallback title via the framework's singleton-key
        // dedupe. <title> carries data-rask-key="tag:title" so we match the body, not the
        // opening tag verbatim.
        Assert.Contains(">Guides &#x2014; Rask</title>", html);
        Assert.Contains("charset=\"utf-8\"", html);
        Assert.Contains("viewport", html);
        // One stylesheet, compiled by Rask.Tailwind from this project's own source. It replaced three
        // (Bootstrap, the design tokens, global.css) whose CASCADE ORDER decided the outcome.
        Assert.Contains("/css/app.css", html);
    }

    [Fact]
    public void LiveRender_EmitsRouterAndRuntimeScriptSlot_InBody()
    {
        var html = RaskTest.RenderDocument(new Shared.App(), TestServices.Default()).Html;

        // Router rendered the matched chain — ShowcaseLayout contributes the navbar.
        Assert.Contains("app-brand", html);
        Assert.Contains("</body>", html);
        Assert.EndsWith("</html>", html.TrimEnd());
    }

    [Fact]
    public void LiveRender_UnmatchedRoute_StillProducesHtml()
    {
        var routeState = new RouteState { Path = "/__no_such_path" };
        var html = RaskTest.RenderDocument(new Shared.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title ", html);
    }
}
