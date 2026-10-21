using System.Text;
using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     The kit on paper: controls and chrome print as nothing, a table runs on whole, an accordion prints open — and
///     not one of those rules reaches a screen. Read from the COMPILED sheet, where Tailwind has merged the layers.
/// </summary>
public sealed partial class UiPrintTests
{
    private const string Print = "@media print{";

    [Theory]
    [InlineData("[data-ui-button]", "display:none")]
    [InlineData("[data-ui-badge-close]", "display:none")]
    [InlineData("[data-ui-pagination]", "display:none")]
    [InlineData("[data-ui-sidebar]", "display:none")]
    [InlineData("[data-ui-header]", "display:none")]
    [InlineData("[data-ui-toast]", "display:none")]
    [InlineData("[data-ui-toast-group]", "display:none")]
    [InlineData("[data-ui-tooltip-content]", "display:none")]
    [InlineData("[data-ui-menu]", "display:none")]
    [InlineData("[data-ui-popover]", "display:none")]
    [InlineData("[data-ui-modal]::backdrop", "background:0 0")]
    [InlineData("[data-ui-main]", "padding:0")]
    [InlineData("[data-ui-main]", "overflow:visible")]
    [InlineData(":has(>[data-ui-main])", "height:auto")]
    [InlineData(":has(>[data-ui-table])", "overflow:visible")]
    [InlineData("[data-ui-table] :is(th,td,.truncate)", "white-space:normal")]
    [InlineData("[data-ui-table]", "table-layout:auto")]
    [InlineData("[data-ui-table]>thead", "display:table-header-group")]
    [InlineData("[data-ui-table] tr", "break-inside:avoid")]
    [InlineData("[data-ui-accordion-item]::details-content", "content-visibility:visible")]
    [InlineData("[data-ui-accordion-item]::details-content", "height:auto")]
    [InlineData("[data-ui-accordion-heading]>[data-ui-icon]", "display:none")]
    public void A_print_default_is_in_the_sheet_and_only_on_paper(string selector, string declaration)
    {
        var (paper, screen) = Split(UiStylesheet.Css);

        var printed = Sets(paper, selector, declaration);
        var shown = Sets(screen, selector, declaration);

        Assert.True(printed, $"No `{selector} {{ {declaration} }}` inside @media print.");
        Assert.False(shown, $"`{selector} {{ {declaration} }}` reaches the screen.");
    }

    [Fact]
    public void The_sheet_has_print_rules_to_read_at_all()
    {
        var css = UiStylesheet.Css;

        var (paper, _) = Split(css);

        // Vacuous-pass guard: were the block renamed or minified differently, every "not on screen" above would hold.
        Assert.Contains("[data-ui-button]", paper, StringComparison.Ordinal);
    }

    // The sheet's `@media print { … }` blocks, and everything else.
    private static (string Paper, string Screen) Split(string css)
    {
        var paper = new StringBuilder();
        var screen = new StringBuilder();
        var at = 0;
        for (var start = css.IndexOf(Print, StringComparison.Ordinal); start >= 0; start = css.IndexOf(Print, at, StringComparison.Ordinal))
        {
            var end = Closing(css, start + Print.Length - 1);
            screen.Append(css, at, start - at);
            paper.Append(css, start + Print.Length, end - start - Print.Length);
            at = end + 1;
        }

        screen.Append(css, at, css.Length - at);
        return (paper.ToString(), screen.ToString());
    }

    private static int Closing(string css, int open)
    {
        var depth = 0;
        for (var i = open; i < css.Length; i++)
        {
            depth += css[i] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("An @media print block is not closed.");
    }

    // Whether a rule of the sheet lists the selector and sets the declaration.
    private static bool Sets(string css, string selector, string declaration) =>
        Rule().Matches(css).Any(rule =>
            Selectors(rule.Groups[1].Value).Contains(selector, StringComparer.Ordinal)
            && rule.Groups[2].Value.Split(';').Contains(declaration, StringComparer.Ordinal));

    // A selector list split at its own commas, not at those inside :is(…) or :has(…).
    private static IEnumerable<string> Selectors(string list)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            depth += list[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (list[i] == ',' && depth == 0)
            {
                yield return list[start..i].Trim();
                start = i + 1;
            }
        }

        yield return list[start..].Trim();
    }

    [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}")]
    private static partial Regex Rule();
}
