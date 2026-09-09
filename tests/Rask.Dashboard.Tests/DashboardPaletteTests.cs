using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
///     The console draws with two vocabularies on one page — its own <c>ui-*</c> utilities and the kit's
///     daisyUI classes — and these hold them to one palette.
/// </summary>
/// <remarks>
///     <para>
///     <c>dashboard.css</c> used to carry its own near-white ladder in literal <c>oklch()</c>. Every token
///     was valid, every class name in the markup was correct, and the whole unit suite was green — while
///     an operator on a dark-mode machine got a console whose chrome followed daisyUI into the dark and
///     whose every label stayed near-black, because <c>UiShell</c>'s <c>bg-base-200</c> and this sheet's
///     <c>text-ui-ink</c> were answering to different palettes. Markup assertions cannot see that: it is
///     entirely a question of what the tokens resolve to.
///     </para>
///     <para>
///     So the invariants are about resolution, not about class names. The palette comes from daisyUI, the
///     theme is named rather than inherited from the reader's OS, and a colour daisyUI publishes as a
///     SURFACE is never handed straight to a text utility.
///     </para>
/// </remarks>
public sealed partial class DashboardPaletteTests : global::Rask.Core.RaskMarkup
{
    /// <summary>
    ///     daisyUI's status and neutral colours are backgrounds — each is meant to be paired with its own
    ///     <c>-content</c> on top — and every one of them fails WCAG AA read as text on a light ground:
    ///     <c>--color-warning</c> measures 1.76:1 on white and <c>--color-error</c> 2.87:1, while
    ///     <c>--color-neutral</c> is oklch(14%), DARKER than the body text it would be muting.
    /// </summary>
    private static readonly string[] SurfaceOnly =
    [
        "--color-warning",
        "--color-error",
        "--color-success",
        "--color-info",
        "--color-neutral",
    ];

    [Fact]
    public void The_palette_is_daisyUIs_and_declares_no_literal_colour()
    {
        var theme = ThemeBlock();

        // Vacuous-pass guard: a parser that stopped matching would pass every case below in silence.
        Assert.NotEmpty(theme);

        foreach (var (token, value) in theme)
        {
            Assert.True(
                value.Contains("var(--color-", StringComparison.Ordinal),
                $"dashboard.css declares {token}: {value} — a colour of its own rather than one of "
                + "daisyUI's semantic variables. The console renders the kit's components beside its own "
                + "utilities, so a second palette here is not a re-skin, it is two designs on one page: "
                + "that is how the console's chrome followed daisyUI into dark mode while its labels "
                + "stayed near-black.");

            Assert.False(
                Regex.IsMatch(value, @"\b(oklch|rgba?|hsla?|lab|lch)\s*\(|#[0-9a-fA-F]{3,8}\b"),
                $"dashboard.css pins a literal colour in {token}: {value}. Derive it from a daisyUI "
                + "variable instead — color-mix() against --color-base-100 or black keeps the tier tied "
                + "to the theme rather than to a number nobody re-measures.");
        }
    }

    /// <summary>
    ///     A token the console writes as TEXT must not be a bare alias of a daisyUI surface colour.
    /// </summary>
    /// <remarks>
    ///     This is the trap the <c>-ink</c> twins in the sheet exist to avoid, and it is invisible to
    ///     everything else: <c>text-ui-warn</c> against <c>--color-warning</c> renders a perfectly
    ///     plausible amber that happens to be unreadable, and it looks fine to anyone who already knows
    ///     what the label says. Scanned from the pages rather than listed here, so a new
    ///     <c>text-ui-something</c> is covered the day it is written.
    /// </remarks>
    [Fact]
    public void No_text_utility_resolves_to_a_daisyUI_surface_colour()
    {
        var theme = ThemeBlock();
        var written = TextTokensWrittenByThePages();

        Assert.NotEmpty(written);

        foreach (var token in written)
        {
            Assert.True(
                theme.ContainsKey(token),
                $"The pages write text-{token[8..]} but dashboard.css declares no {token}. Tailwind "
                + "emits a utility only for a token it can see, so that class renders with no colour "
                + "at all.");

            var value = theme[token].Trim();
            foreach (var surface in SurfaceOnly)
            {
                Assert.False(
                    string.Equals(value, $"var({surface})", StringComparison.Ordinal),
                    $"dashboard.css aliases {token} straight to {surface}, and the pages write it as "
                    + "text. daisyUI publishes that colour as a background to pair with its own "
                    + $"-content, and it fails AA read on the console's light ground. Darken it toward "
                    + "black in oklab instead, the way --color-ui-warn-ink and --color-ui-danger do.");
            }
        }
    }

    /// <summary>
    ///     The theme scope reaches <c>:root</c>, and it names a theme.
    /// </summary>
    /// <remarks>
    ///     Two separate failures, both silent. Tailwind emits the console's <c>@theme</c> aliases at
    ///     <c>:root</c>, and a custom property's <c>var()</c> is substituted where the declaration applies
    ///     — so without the scope on <c>&lt;html&gt;</c>, <c>--color-base-100</c> is undefined there and
    ///     every alias computes to nothing, leaving a fully laid-out console with no colour in it. And a
    ///     scope with no <c>data-theme</c> matches daisyUI's <c>[data-rask-ui]:not([data-theme])</c>,
    ///     which follows <c>prefers-color-scheme</c> — repainting a surface whose contrast ratios are all
    ///     measured against a white ground.
    /// </remarks>
    [Fact]
    public void The_document_element_carries_the_theme_scope_and_names_the_theme()
    {
        var page = RaskTest.RenderDocument(RaskDashboardShell).Html;

        Assert.Contains("data-rask-ui", page, StringComparison.Ordinal);
        Assert.Contains("data-theme=\"light\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     …and so does the shell inside it, which is the second element carrying the scope.
    /// </summary>
    /// <remarks>
    ///     Pinning <c>&lt;html&gt;</c> alone is not enough and reads as though it were.
    ///     <c>[data-rask-ui]:not([data-theme])</c> matches on the ELEMENT, so <c>UiShell</c>'s own div
    ///     re-declares <c>--color-base-*</c> dark for everything beneath it while the aliases computed at
    ///     <c>:root</c> stay light — the same split rendering, one level down.
    /// </remarks>
    [Fact]
    public async Task The_shell_inside_it_pins_the_same_theme()
    {
        // Development, because the fail-closed default denies everyone in Production and the chrome under
        // test sits behind that policy.
        await using var h = new DashboardHarness(environment: Environments.Development);
        h.Get<RouteState>().Path = "/_rask";

        var page = RaskTest.RenderDocument(RaskDashboardShell, h.Services);

        Assert.True(
            page.Exists(".rask-ops[data-rask-ui][data-theme=\"light\"]"),
            "UiShell renders the kit's theme scope with no theme named, so daisyUI falls back to "
            + "prefers-color-scheme for everything inside it.");
    }

    /// <summary>Every declaration in the sheet's <c>@theme</c> block, by token name.</summary>
    private static Dictionary<string, string> ThemeBlock()
    {
        // Comments stripped first: this file explains its reasoning at length, and several of those
        // comments quote the literal oklch() values they are arguing against.
        var css = Regex.Replace(File.ReadAllText(StylesheetSourcePath()), @"/\*.*?\*/", " ",
            RegexOptions.Singleline);

        var block = Regex.Match(css, @"@theme\s*\{(?<body>[^{}]*(?:\{[^{}]*\}[^{}]*)*)\}");
        Assert.True(block.Success, "dashboard.css has no @theme block.");

        return Regex.Matches(block.Groups["body"].Value, @"(?<name>--[\w-]+)\s*:\s*(?<value>[^;]+);")
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value.Trim(),
                StringComparer.Ordinal);
    }

    /// <summary>The <c>--color-ui-*</c> tokens the console's pages name through a text utility.</summary>
    private static SortedSet<string> TextTokensWrittenByThePages()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(PackageSourcePath(), "*.cs", SearchOption.AllDirectories))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\btext-ui-(?<name>[a-z0-9-]+)\b"))
            {
                found.Add("--color-ui-" + m.Groups["name"].Value);
            }
        }

        return found;
    }

    // Resolved from the compiler rather than copied to the output directory, the way ChromeStylesheetTests
    // finds the showcase's sheet: the file under test is the one the package compiles, not a build artifact
    // of it. The COMPILED sheet is asserted separately, by DashboardStylesheetTests — these read the source
    // because "no literal colour" is a statement about what was written, and Tailwind's output resolves
    // some of it away.
    private static string StylesheetSourcePath([CallerFilePath] string here = "") =>
        Path.Combine(PackageSourcePath(here), "Styles", "dashboard.css");

    private static string PackageSourcePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "Rask.Dashboard");
}
