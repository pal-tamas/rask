using Rask.Core.Routing;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Layout;

/// <summary>
///     One top bar, worn by both <c>/</c> and <c>/docs</c>.
/// </summary>
/// <remarks>
///     <para>
///         They were two bars that agreed on a height and a background and on nothing else: the landing
///         page had a <c>&lt;header&gt;</c>, a bolt, a wordmark and three quiet text links; the docs had
///         daisyUI's <c>navbar</c>, a hamburger, a second brand mark in the display face, a "showcase"
///         pill, a version badge, a live <c>path:</c> readout and a bordered ★ GitHub button.
///     </para>
///     <para>
///         Nothing could have failed over that. Every class name on both was correct, both passed their
///         own assertions, and the only person positioned to see it was a visitor crossing from one to
///         the other. So the assertion here is not "the docs bar has a wordmark" — it is that the two
///         bars are the SAME markup from the wordmark rightwards, which is the thing that was not true
///         and the thing that quietly stops being true again.
///     </para>
/// </remarks>
public sealed class SiteHeaderTests
{
    private static string Render(string path) =>
        Page.Render(
                new global::Rask.Site.App(),
                TestServices.Default(routeState: new RouteState { Path = path }))
            .Html;

    /// <summary>The <c>&lt;header&gt;</c> element, from its opening tag to its close.</summary>
    private static string Header(string html)
    {
        var start = html.IndexOf("<header", StringComparison.Ordinal);
        Assert.True(start >= 0, "the page rendered no <header> at all");

        var end = html.IndexOf("</header>", start, StringComparison.Ordinal);
        Assert.True(end >= 0, "the <header> was never closed");

        return html[start..(end + "</header>".Length)];
    }

    /// <summary>
    ///     The bar from the wordmark rightwards — brand, version badge and the trailing controls.
    /// </summary>
    /// <remarks>
    ///     Everything the two pages are ALLOWED to differ on sits to the left of this cut: the container's
    ///     width class (the docs run full-bleed past a 280px rail; the landing page centres in its own
    ///     1100px column) and the leading slot (the docs' sidebar hamburger, which has to be in the bar
    ///     because that is where a thumb reaches for it). Past the cut there is no licence to differ, and
    ///     a byte comparison is the only form of that assertion that cannot be satisfied by a bar which
    ///     merely resembles the other one.
    /// </remarks>
    private static string FromTheWordmark(string html)
    {
        var header = Header(html);
        var brand = header.IndexOf("app-brand", StringComparison.Ordinal);
        Assert.True(brand >= 0, $"no .app-brand in the top bar:\n{header}");

        // The moon's tooltip takes the next id of the process, so two renders differ in it and in nothing else.
        return System.Text.RegularExpressions.Regex.Replace(header[brand..], "ui-tooltip-\\d+", "ui-tooltip-n");
    }

    [Fact]
    public void The_docs_wear_the_landing_page_bar()
    {
        Assert.Equal(
            FromTheWordmark(Render("/")),
            FromTheWordmark(Render(global::Rask.Site.Routes.GuidesIndexPage())));
    }

    [Fact]
    public void Both_bars_carry_the_version_the_site_is_built_on()
    {
        // The badge reads the version off the assembly MinVer stamped. It said "v1.0.0" on every page of
        // rask.sh for as long as the badge existed, because RaskVersion lives in Rask.Core, Core is
        // IsPackable=false, and MinVer was referenced only by packable projects — see
        // RaskVersionTests.The_current_version_matches_the_packable_host_version, which is what guards the number itself.
        // This guards that both bars ask for it, and neither prints a literal.
        var expected = $"v{RaskVersion.Current}";

        Assert.Contains(expected, Header(Render("/")), StringComparison.Ordinal);
        Assert.Contains(
            expected,
            Header(Render(global::Rask.Site.Routes.GuidesIndexPage())),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_docs_bar_carries_the_sidebar_toggle()
    {
        Assert.DoesNotContain("hamburger-btn", Header(Render("/")), StringComparison.Ordinal);
        Assert.Contains(
            "hamburger-btn",
            Header(Render(global::Rask.Site.Routes.GuidesIndexPage())),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/docs/")]
    public void The_bar_carries_a_moon_that_flips_dark_mode_without_a_handler(string path)
    {
        var header = Header(Render(path));

        var marker = header.IndexOf("data-appearance-toggle", StringComparison.Ordinal);
        Assert.True(marker >= 0, $"no moon in the top bar:\n{header}");
        var start = header.LastIndexOf("<button", marker, StringComparison.Ordinal);
        var moon = header[start..header.IndexOf("</button>", marker, StringComparison.Ordinal)];

        Assert.Contains("type=\"button\"", moon, StringComparison.Ordinal);
        // Named by its tooltip, as Flux names an icon button: by reference, not by a label of its own.
        var tooltip = System.Text.RegularExpressions.Regex.Match(moon, "aria-labelledby=\"(ui-tooltip-\\d+)\"").Groups[1].Value;
        Assert.Contains($"<div id=\"{tooltip}\"", header, StringComparison.Ordinal);
        Assert.Contains(">Toggle dark mode <span class=\"ps-1 text-zinc-300\">D</span></div>", header, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-label=", moon, StringComparison.Ordinal);
        Assert.Contains("aria-keyshortcuts=\"D\"", moon, StringComparison.Ordinal);
        Assert.Contains("onclick=\"Rask.dark=!Rask.dark\"", moon, StringComparison.Ordinal);
        Assert.Contains("data-ui-icon", moon, StringComparison.Ordinal);
        // Handler ids are positional: one in the chrome of every page moves every id after it.
        Assert.DoesNotContain("data-rask-on-", moon, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("theme-controller")]
    [InlineData("popovertarget")]
    [InlineData(">Theme<")]
    public void The_bar_dropped_the_theme_picker(string retired)
    {
        var landing = Header(Render("/"));
        var docs = Header(Render(global::Rask.Site.Routes.GuidesIndexPage()));

        Assert.DoesNotContain(retired, landing, StringComparison.Ordinal);
        Assert.DoesNotContain(retired, docs, StringComparison.Ordinal);
    }

    [Fact]
    public void The_appearance_script_runs_before_any_stylesheet_and_the_shortcut_is_there_once()
    {
        var app = new global::Rask.Site.App();

        var html = Page.RenderDocument(app, TestServices.Default(routeState: new RouteState { Path = "/" })).Html;
        var script = html.IndexOf("Object.defineProperty(R,'appearance'", StringComparison.Ordinal);
        var sheet = html.IndexOf("rel=\"stylesheet\"", StringComparison.Ordinal);

        Assert.True(script >= 0, "the page's head carries no appearance script, so it cannot start dark.");
        Assert.True(script < sheet, "the appearance script comes after a stylesheet: a dark page would flash light.");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "raskAppearanceKey=true"));
    }

    [Theory]
    // daisyUI's navbar halves, which drew the docs bar and nothing else on the site.
    [InlineData("navbar-start")]
    [InlineData("navbar-end")]
    // The pill that told a reader of the docs they were in "the showcase", a word the site uses nowhere else.
    [InlineData(">showcase<")]
    // The live route readout. PathDisplay itself stays — it is the worked example behind
    // docs/routing.md's "Reacting to navigation" — but it is not chrome.
    [InlineData("path: ")]
    public void The_docs_bar_dropped_its_retired_piece(string retired)
    {
        Assert.DoesNotContain(
            retired,
            Header(Render(global::Rask.Site.Routes.GuidesIndexPage())),
            StringComparison.Ordinal);
    }
}
