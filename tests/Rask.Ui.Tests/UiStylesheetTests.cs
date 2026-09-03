using System.Text.RegularExpressions;

namespace Rask.Ui.Tests;

/// <summary>
///     The shipped stylesheet. These assert on the COMPILED artifact rather than on the source it was
///     built from: the sheet is produced by Tailwind and daisyUI at this project's build and embedded as a
///     resource, so the source stating an intention is not evidence the bytes an app receives carry it.
/// </summary>
public sealed class UiStylesheetTests
{
    [Fact]
    public void The_sheet_shipped_at_all()
    {
        // Empty is the documented behaviour when the resource is missing, which keeps an app startable —
        // and would otherwise make every assertion below vacuously true.
        Assert.NotEqual("", UiStylesheet.Css);
    }

    [Fact]
    public void The_theme_is_scoped_to_the_opt_in_attribute()
    {
        Assert.Contains($"[{UiStylesheet.ThemeScopeAttribute}]", UiStylesheet.Css);
    }

    [Fact]
    public void The_theme_never_reaches_a_document_that_did_not_ask_for_it()
    {
        // daisyUI defines its palette at the document root by default, which would mean that merely
        // REFERENCING this package repaints the background and text colour of an application that wanted
        // a button. The kit scopes the theme instead, and this is what holds that line: no rule may
        // introduce a base colour at a bare `:root` or `[data-theme]`, only inside the opt-in scope.
        foreach (var rule in Rules(UiStylesheet.Css))
        {
            // DEFINES one, not merely uses one. Component rules reference var(--color-base-content) all
            // over the sheet and should: outside the scope those resolve to nothing, which is the whole
            // mechanism. Only a declaration would put a colour on a page that never opted in.
            if (!Regex.IsMatch(rule.Body, @"(^|;)\s*--color-base-[\w-]*\s*:"))
            {
                continue;
            }

            Assert.Contains(UiStylesheet.ThemeScopeAttribute, rule.Selector);
        }
    }

    [Fact]
    public void The_sheet_carries_no_preflight_and_no_document_rules()
    {
        // The kit ships utilities and components, never a reset: an app owns its own document, and a
        // second reset arriving from a library restyles pages that never asked for it.
        foreach (var rule in Rules(UiStylesheet.Css))
        {
            Assert.DoesNotMatch(new Regex(@"(^|,)\s*(html|body)\s*(,|$)"), rule.Selector);
        }
    }

    [Fact]
    public void The_theme_can_be_switched_without_javascript()
    {
        // daisyUI matches a checked input and an explicit attribute, which is what lets a surface offer a
        // theme toggle as a checkbox rather than as a script.
        Assert.Contains("theme-controller", UiStylesheet.Css);
        Assert.Contains("[data-theme=dark]", UiStylesheet.Css);
    }

    private static IEnumerable<(string Selector, string Body)> Rules(string css)
    {
        foreach (Match m in Regex.Matches(css, @"(?:^|[}\s;])([^{}@]+)\{([^{}]*)\}"))
        {
            yield return (m.Groups[1].Value.Trim(), m.Groups[2].Value);
        }
    }
}
