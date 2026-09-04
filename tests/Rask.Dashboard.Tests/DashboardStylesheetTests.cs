using System.Reflection;
using System.Text.RegularExpressions;
using Rask.Dashboard.Pages;
using Rask.Ui;

namespace Rask.Dashboard.Tests;

// Asserts on the SHIPPED artifact, not on the build that produced it. The stylesheet is compiled by
// Tailwind during this package's build and embedded as a manifest resource; DashboardLayout reads it
// back and inlines it. Every link in that chain is silent when it breaks — a missing resource yields
// an unstyled console rather than an error (deliberately: an app should not fail to start because its
// dashboard has no CSS), and a Tailwind run that scanned the wrong directory yields a stylesheet that
// is present, valid, and missing every class the pages actually use.
public class DashboardStylesheetTests
{
    // BOTH sheets, because both are what the console inlines. Tailwind scans the project it runs in, so
    // since the kit moved to Rask.Ui the classes its components write are compiled into ITS sheet and the
    // classes these pages write into this one — and neither build can see the other's markup. Asserting
    // against only this package's half is how `sm:grid-cols-5` (written by UiMetricRow, and pinned below)
    // would read as missing on a console that renders it perfectly.
    private static string Css() => UiStylesheet.Css + "\n" + DashboardCss();

    private static string DashboardCss()
    {
        var assembly = typeof(DashboardLayout).Assembly;
        using var stream = assembly.GetManifestResourceStream("Rask.Dashboard.dashboard.css");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void The_stylesheet_is_embedded_and_not_empty() => Assert.NotEmpty(DashboardCss());

    [Fact]
    public void The_kits_stylesheet_ships_too()
    {
        // Separately from the console's. It travels in a different assembly, compiled by a different
        // Tailwind run, and an empty one leaves every shared component unstyled while this package's own
        // sheet is perfectly fine — which looks like a layout bug, not a packaging one.
        Assert.NotEmpty(UiStylesheet.Css);
    }

    [Fact]
    public void The_kits_stylesheet_carries_no_reset()
    {
        // The kit is consumed by applications that own their own document and run their own Tailwind. A
        // preflight arriving from a library restyles pages that never asked for it — which is exactly
        // what happened to host applications while the console rendered inside their document. The
        // console's own sheet carries the reset (asserted below); the kit's must not.
        // Asserted on the SELECTOR, not on a declaration. A reset is `box-sizing` applied to everything —
        // `*`, `html`, `body`, `::before`/`::after` — and daisyUI legitimately sets the same property inside
        // ordinary class rules, so matching the declaration text alone reports those as a preflight.
        foreach (Match rule in Regex.Matches(UiStylesheet.Css, @"(?:^|[}\s;])([^{}@]+)\{([^{}]*)\}"))
        {
            var selector = rule.Groups[1].Value.Trim();
            var universal = Regex.IsMatch(selector, @"(^|,)\s*(\*|html|body)\b")
                            || selector.Contains("*,", StringComparison.Ordinal);

            Assert.False(
                universal && rule.Groups[2].Value.Contains("box-sizing", StringComparison.Ordinal),
                $"The kit's sheet resets box-sizing on '{selector}'.");
        }
    }

    // Tailwind emits only the utilities it finds in the sources it scans, so a rule that scans nothing
    // still produces a valid, useless file. These are picked from the theme and from the pages, so a
    // scan that missed Pages/ or lost the @theme block fails here rather than in a screenshot.
    // EVERY token in the @theme block, not a sample of them. The stylesheet's own comment claims this test
    // pins the token names; while it asserted three of them that claim was false, and renaming any of the
    // other seven — each load-bearing across Ui/ — was green. A token is only emitted when something uses
    // it, so this doubles as the check that each one is actually referenced by a utility somewhere.
    [Theory]
    [InlineData("--color-ui-bg")]       // the @theme block reached the output
    [InlineData("--color-ui-well")]
    [InlineData("--color-ui-line")]
    [InlineData("--color-ui-ink")]
    [InlineData("--color-ui-muted")]
    [InlineData("--color-ui-brand")]
    [InlineData("--color-ui-warn")]
    [InlineData("--color-ui-danger")]
    [InlineData("--color-ui-warn-ink")] // the text-on-light twin; the fill above fails 4.5:1 as text
    [InlineData("bg-ui-well")]          // a themed utility, so the theme is wired to utilities
    [InlineData("text-ui-muted")]
    [InlineData("tabular-nums")]         // used by every counter on the console
    [InlineData("animate-spin")]         // the loading spinner, which ships no asset of its own
    // Backslash because a variant's colon is CSS-escaped in the emitted selector (`.sm\:grid-cols-5`), so
    // the obvious `sm:grid-cols-5` never matches and the assertion fails on a stylesheet that is correct.
    [InlineData(@"sm\:grid-cols-5")]     // the metric row's responsive shape, which only Ui/ references
    [InlineData("min-h-11")]             // the 44px touch target the mobile-first layout depends on
    public void The_stylesheet_carries_the_classes_the_pages_use(string fragment) =>
        Assert.Contains(fragment, Css(), StringComparison.Ordinal);

    // The console is a mounted application with its own document, so it carries a full reset. This was
    // the reverse assertion while the console rendered inside the host's document: the reset had to be
    // hand-scoped, because a global one landed on the host application's own pages. Pinned in this
    // direction so that reverting the mount without reverting the stylesheet fails here rather than in
    // somebody's browser.
    [Fact]
    public void The_console_owns_its_document_so_it_ships_a_full_reset() =>
        Assert.Contains("html,:host", Css(), StringComparison.Ordinal);

    [Fact]
    public void The_document_carries_the_console_background() =>
        Assert.Contains("--color-ui-bg", Css(), StringComparison.Ordinal);
}
