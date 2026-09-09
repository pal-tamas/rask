using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Rask.Site.Tests.Layout;

/// <summary>
///     The showcase's chrome is drawn by utilities and by daisyUI, and <c>wwwroot/global.css</c> is not
///     allowed to take any of it back.
/// </summary>
/// <remarks>
///     <para>
///     That file is a plain, UNLAYERED stylesheet, linked after everything else. Unlayered rules outrank
///     every layered one on the page — all of Tailwind's utilities and all of daisyUI's components live
///     in <c>@layer</c>s — so a single selector here silently beats the classes written in the markup,
///     with no specificity fight to notice and nothing in a build or a class-name assertion to see it.
///     </para>
///     <para>
///     It is not a hypothetical. <c>.app-navbar { background: rgba(20, 16, 31, .82) }</c> survived the
///     showcase's move to the kit's light palette and beat the <c>bg-ui-bg</c> on the same element: the
///     top bar rendered near-black while its own text stayed <c>text-ui-ink</c> (base-content, near-black
///     in this theme), putting the wordmark, the "showcase" pill and the route readout at roughly 1.4:1.
///     Every class name in the markup was correct, so the unit suite, the browser suite and the build all
///     stayed green. Same shape as #1033, one cascade layer up.
///     </para>
///     <para>
///     So the rule for this file: it styles <c>:root</c>, shell tags, and markup another component
///     renders through <c>Raw()</c> (the syntax-highlight token spans, the Markdig guide HTML) — things a
///     scoped <c>{Component}.css</c> can never reach. A chrome element whose classes say what it looks
///     like is off limits, and the names below stay on those elements as selectors for the tests, not as
///     style hooks.
///     </para>
/// </remarks>
public sealed class ChromeStylesheetTests
{
    /// <summary>
    ///     Chrome hooks whose appearance is settled in the markup. <c>.side-nav-link</c> and
    ///     <c>.nav-group-*</c> joined this list with the sidebar's conversion to daisyUI's <c>menu</c>;
    ///     these arrive with the top bar's to <c>navbar</c>.
    /// </summary>
    public static TheoryData<string> UtilityStyledHooks =>
    [
        "app-navbar",
        "app-shell",
        "hamburger-btn",
        "page-main",
        "rask-badge",
        "sample-card",
        "sample-result-col",
        "side-nav-link",
        "nav-group",
        "nav-group-toggle",
        "nav-group-items",
        // Bootstrap's offcanvas went with Rask.Bootstrap; the drawer is an <aside> the layout renders
        // itself. A rule naming one of these matches nothing at all, which is its own kind of wrong —
        // `.side-nav.offcanvas-md .offcanvas-body` capped the desktop rail's height and had silently
        // stopped doing so, leaving a 4,300px sticky column whose bottom entries no scroll could reach.
        "offcanvas",
        "offcanvas-md",
        "offcanvas-body",
        // Deleted with the component that rendered it: nothing has emitted a "See also" pill since the
        // demo pages folded into the guides.
        "see-also-link",
    ];

    [Theory]
    [MemberData(nameof(UtilityStyledHooks))]
    public void GlobalStylesheet_DeclaresNoRuleFor(string className)
    {
        var offenders = Selectors()
            .Where(s => Regex.IsMatch(s, $@"\.{Regex.Escape(className)}(?![\w-])"))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"wwwroot/global.css styles .{className}, which the markup styles with utilities. This sheet "
            + "is unlayered and linked last, so its rule wins over every utility on that element and the "
            + "class in the markup becomes decoration. Move the declaration onto the element:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheSheet_StillHasSelectorsToCheck()
    {
        // Vacuous-pass guard: a parser that stops matching would pass every case above in silence.
        var selectors = Selectors();
        Assert.NotEmpty(selectors);
        Assert.Contains(selectors, s => s.Contains(".markdown-body", StringComparison.Ordinal));
    }

    /// <summary>Every selector prelude in the sheet, at-rules (<c>@media</c>, <c>@keyframes</c>) excluded.</summary>
    private static List<string> Selectors()
    {
        var css = Regex.Replace(File.ReadAllText(StylesheetPath()), @"/\*.*?\*/", " ", RegexOptions.Singleline);

        return Regex.Matches(css, @"([^{}]+)\{")
            .Select(m => Regex.Replace(m.Groups[1].Value, @"\s+", " ").Trim())
            .Where(s => s.Length > 0 && !s.StartsWith('@'))
            .ToList();
    }

    // Resolved from the compiler rather than copied to the output directory, the way DemoMarkupGoldenTests
    // finds its golden: the sheet under test is the one the app serves, not a build artifact of it.
    private static string StylesheetPath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "Rask.Site", "wwwroot", "global.css");
}
