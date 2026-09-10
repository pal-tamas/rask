using Rask.Core.Routing;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

/// <summary>
///     The one PWA page, and the twelve URLs it absorbed.
/// </summary>
/// <remarks>
///     Thirteen sidebar rows — each a page whose whole body was a heading, a paragraph and one
///     <c>CodeSample</c> — became one page with thirteen sections. The consolidation is only safe because
///     <c>[Route]</c> repeats: every old URL still resolves here, so a link that was shared, bookmarked or
///     indexed is not a 404. That is the half of the change nothing else would notice — a dropped route
///     fails in someone else's browser history, months later, and never on this machine.
/// </remarks>
public sealed class PwaPageTests
{
    /// <summary>Every URL the page must answer. The first is canonical; the rest are what it absorbed.</summary>
    private static readonly string[] Urls =
    [
        "pwa",
        "install",
        "wake-lock",
        "orientation",
        "fullscreen",
        "picture-in-picture",
        "eye-dropper",
        "idle",
        "media-devices",
        "serial",
        "usb",
        "hid",
        "bluetooth",
    ];

    [Fact]
    public void It_answers_every_url_the_thirteen_pages_used_to()
    {
        var declared = typeof(PwaPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(a => a.Template.Trim('/'))
            .ToHashSet(StringComparer.Ordinal);

        // Vacuous-pass guard: a reflection read that stopped working would make every check below pass.
        Assert.NotEmpty(declared);

        var missing = Urls.Where(u => !declared.Contains(u)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"PwaPage no longer answers {string.Join(", ", missing)}. Each of those was a page of its own "
            + "before the thirteen demos were consolidated, so dropping the route turns every link anyone "
            + "kept into a 404 — add the [Route] back rather than the page.");
    }

    [Fact]
    public void The_canonical_url_is_the_one_it_is_named_for()
    {
        // Rask treats the FIRST declared [Route] as canonical — it is what Url()/Go() generate and what the
        // page's own <link rel="canonical"> points at. The alternates are matched but never generated, so
        // which one leads is the difference between one indexed page and thirteen competing duplicates.
        var first = typeof(PwaPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .First()
            .Template
            .Trim('/');

        Assert.Equal("pwa", first);
    }

    [Fact]
    public void Every_section_in_the_rail_exists_on_the_page()
    {
        // The rail and the sections are generated from ONE list, and this is the guard on that staying
        // true. A table of contents assembled beside the content it indexes goes stale silently: nothing
        // fails when a link points at a heading that moved, the reader just scrolls nowhere.
        //
        // Rendered through the app so the route resolves and CodeSample gets its services — the page
        // cannot be newed up and rendered on its own, because CodeSample is DI-constructed.
        var routeState = new RouteState { Path = global::Rask.Site.Features.Routes.PwaPage() };
        var html = RaskTest.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.Contains("On this page", html, StringComparison.Ordinal);

        foreach (var slug in Sections)
        {
            Assert.Contains($"href=\"#{slug}\"", html, StringComparison.Ordinal);
            Assert.Contains($"id=\"{slug}\"", html, StringComparison.Ordinal);
            Assert.Contains($"data-section=\"{slug}\"", html, StringComparison.Ordinal);
        }
    }

    /// <summary>The thirteen demos, by anchor.</summary>
    private static readonly string[] Sections =
    [
        "notifications", "install", "wake-lock", "orientation", "fullscreen", "picture-in-picture",
        "eye-dropper", "idle", "media-devices", "serial", "usb", "hid", "bluetooth",
    ];
}
