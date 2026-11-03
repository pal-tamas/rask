using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Rask.Core.Routing;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Layout;

/// <summary>
///     On a notched phone in landscape the side insets are applied ONCE: by the body's padding in
///     <c>wwwroot/global.css</c>, and not again by the kit's chrome inside it.
/// </summary>
/// <remarks>
///     The kit pads its header and sidebar with <c>max(its padding, env(safe-area-inset-*))</c> (ui.css, NATIVE
///     FEEL), and the boot viewport says <c>viewport-fit=cover</c>, so both sides of the notch are live. The body
///     already keeps everything in flow out of the notch, so a kit part in flow that kept its own side inset would
///     stand a second notch's width further in. Measured with every <c>env()</c> rewritten to an iPhone's insets
///     (47px a side in landscape): the bar, the docked sidebar and the main each start at 47px, once.
/// </remarks>
public sealed class SafeAreaInsetTests
{
    public static TheoryData<string> LayoutPages =>
    [
        global::Rask.Site.Routes.GuidesIndexPage().ToString(),
        global::Rask.Site.Routes.UiHeaderPage().ToString(),
    ];

    [Fact]
    public void The_body_keeps_the_page_out_of_the_notch_on_both_sides()
    {
        var css = Regex.Replace(File.ReadAllText(StylesheetPath()), @"/\*.*?\*/", " ", RegexOptions.Singleline);

        var body = string.Concat(Regex.Matches(css, @"(?m)^body\s*\{([^}]*)\}").Select(m => m.Groups[1].Value));

        Assert.Contains("padding-left: env(safe-area-inset-left)", body, StringComparison.Ordinal);
        Assert.Contains("padding-right: env(safe-area-inset-right)", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LayoutPages))]
    public void The_layout_sidebar_sets_its_own_sides_so_the_kit_adds_no_second_notch_inset(string path)
    {
        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: new RouteState { Path = path })).Html;

        var sidebar = Regex.Match(html, @"<div\b[^>]*\bdata-ui-sidebar(?![\w-])[^>]*>").Value;
        var classes = Regex.Match(sidebar, "class=\"([^\"]*)\"").Groups[1].Value.Split(' ');

        // An important px-* utility beats ui.css's `max(1rem, env(safe-area-inset-*))`, which would add the notch
        // again inside the body's own inset.
        Assert.NotEmpty(sidebar);
        Assert.Contains(classes, c => Regex.IsMatch(c, @"^px-[\w.\[\]()-]+!$"));
    }

    [Theory]
    [MemberData(nameof(LayoutPages))]
    public void The_site_bar_is_not_a_kit_header_with_a_side_inset_of_its_own(string path)
    {
        var html = Page.Render(new global::Rask.Site.App(), TestServices.Default(routeState: new RouteState { Path = path })).Html;

        var bar = Regex.Match(html, @"<header\b[^>]*\bapp-navbar\b[^>]*>").Value;

        Assert.NotEmpty(bar);
        Assert.DoesNotContain("data-ui-header", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("safe-area-inset-left", bar, StringComparison.Ordinal);
        Assert.DoesNotContain("safe-area-inset-right", bar, StringComparison.Ordinal);
    }

    // Resolved from the compiler, as ChromeStylesheetTests does: the sheet the app serves, not a copy of it.
    private static string StylesheetPath([CallerFilePath] string here = "") =>
        Path.Combine(
            Path.GetDirectoryName(here)!, "..", "..", "..", "src", "Rask.Site", "wwwroot", "global.css");
}
