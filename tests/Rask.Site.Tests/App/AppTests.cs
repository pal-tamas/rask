using Rask.Core.Routing;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.App;

public sealed class AppTests
{
    // The showcase root. These assertions are about the SHOWCASE chrome — the navbar brand, the guides
    // title — which used to be what "/" served, back when the guides were their own app. "/" is the
    // landing page now, so rendering there asserts the wrong document; the route is taken from the
    // generated helper so the next prefix move does not need this comment written again.
    private static RouteState ShowcaseRoot => new() { Path = global::Rask.Site.Features.Routes.GuidesIndexPage() };

    [Fact]
    public void A_live_render_starts_with_the_doctype_and_an_en_html_lang()
    {
        var html = Test.RenderDocument(new global::Rask.Site.App(), TestServices.Default()).Html;

        Assert.StartsWith("<!DOCTYPE html>", html);
        // data-rask-ui turns the kit's theme on for the document; the kit scopes daisyUI's palette to
        // it so that referencing the package cannot repaint an app that only wanted a button.
        Assert.Contains("<html lang=\"en\" data-rask-ui=\"\">", html);
        Assert.Contains("<body class=\"bg-ui-well\">", html);
    }

    [Fact]
    public void A_live_render_emits_the_stylesheet_and_meta_in_the_head()
    {
        var html = Test.RenderDocument(
            new global::Rask.Site.App(), TestServices.Default(routeState: ShowcaseRoot)).Html;

        // Title body content is HTML-encoded: literal "—" → "&#x2014;". GuidesIndexPage
        // overrides App's fallback title via the framework's singleton-key
        // dedupe. <title> carries data-rask-key="tag:title" so we match the body, not the
        // opening tag verbatim.
        Assert.Contains(">Docs: guides for building .NET web apps in C# &#x2014; Rask</title>", html);
        Assert.Contains("charset=\"utf-8\"", html);
        Assert.Contains("viewport", html);
        // One stylesheet, compiled by Rask.Tailwind from this project's own source. It replaced three
        // (Bootstrap, the design tokens, global.css) whose CASCADE ORDER decided the outcome.
        Assert.Contains("/css/app.css", html);
    }

    [Fact]
    public void A_live_render_emits_the_router_and_runtime_script_slot_in_the_body()
    {
        var html = Test.RenderDocument(
            new global::Rask.Site.App(), TestServices.Default(routeState: ShowcaseRoot)).Html;

        // Router rendered the matched chain — ShowcaseLayout contributes the navbar.
        Assert.Contains("app-brand", html);
        Assert.Contains("</body>", html);
        Assert.EndsWith("</html>", html.TrimEnd());
    }

    [Fact]
    public void A_live_render_of_an_unmatched_route_still_produces_HTML()
    {
        var routeState = new RouteState { Path = "/__no_such_path" };

        var html = Test.RenderDocument(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title ", html);
    }
}
