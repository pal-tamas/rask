using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.App;

public sealed class AppTests
{
    [Fact]
    public void LiveRender_StartsWithDoctype_AndHtmlEnLang()
    {
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(TestServices.Default());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("<body class=\"bg-body-tertiary\">", html);
    }

    [Fact]
    public void LiveRender_EmitsBootstrapLinksAndMeta_InHead()
    {
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(TestServices.Default());

        // Title body content is HTML-encoded: literal "—" → "&#x2014;". HomePage
        // overrides App's fallback title via the framework's singleton-key dedupe.
        // <title> carries data-rask-key="tag:title" so we match the body, not the
        // opening tag verbatim.
        Assert.Contains(">Welcome &#x2014; Rask</title>", html);
        Assert.Contains("charset=\"utf-8\"", html);
        Assert.Contains("viewport", html);
        Assert.Contains("/lib/bootstrap/bootstrap.min.css", html);
        Assert.Contains("/lib/bootstrap-icons/bootstrap-icons.min.css", html);
    }

    [Fact]
    public void LiveRender_EmitsRouterAndRuntimeScriptSlot_InBody()
    {
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(TestServices.Default());

        // Router rendered the matched chain — ShowcaseLayout contributes the navbar.
        Assert.Contains("navbar-brand", html);
        Assert.Contains("</body>", html);
        Assert.EndsWith("</html>", html.TrimEnd());
    }

    [Fact]
    public void LiveRender_UnmatchedRoute_StillProducesHtml()
    {
        var routeState = new RouteState { Path = "/__no_such_path" };
        var html = new Rask.Example.Shared.App().RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title ", html);
    }
}
