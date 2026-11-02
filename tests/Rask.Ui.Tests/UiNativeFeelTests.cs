using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     The kit's NATIVE FEEL block (owner, 2026-11-02): its controls select nothing, wait for no second tap, open no
///     long-press callout and drag no picture; its overlays keep their scroll; its edges clear the notch — and an app's
///     class still wins on every one of those parts, while the page's content stays text. Read from the COMPILED sheet.
/// </summary>
public sealed partial class UiNativeFeelTests
{
    // What makes a rule one of this block's: a declaration nothing else in the kit's own layer writes.
    private static readonly string[] Declarations =
    [
        "user-select:none", "touch-action:manipulation", "-webkit-touch-callout:none", "-webkit-user-drag:none",
        "overscroll-behavior:contain", "safe-area-inset",
    ];

    // What a reader reads or types: never chrome.
    private static readonly string[] Content =
    [
        "[data-ui-table]", "[data-ui-cell]", "[data-ui-input]", "[data-ui-textarea]", "[data-ui-editor]", "[contenteditable",
        "[data-ui-text]", "[data-ui-heading]", "[data-ui-badge]", "[data-ui-callout]", "[data-ui-toast-dialog]{",
        "[data-ui-navlist-badge]",
    ];

    private static readonly string[] ContentTags = ["table", "td", "th", "input", "textarea", "pre", "code"];

    [Fact]
    public void Every_native_feel_rule_weighs_nothing_so_an_apps_class_wins()
    {
        var rules = NativeFeel();

        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.Matches(@"^:where\(.*\)$", rule.Selector));
        Assert.All(rules, rule => Assert.Equal(rule.Selector.Length - 1, Closing(rule.Selector)));
    }

    [Fact]
    public void Chrome_alone_is_made_unselectable_and_never_what_a_reader_reads_or_types()
    {
        var unselectable = NativeFeel().Where(r => r.Body.Contains("user-select:none", StringComparison.Ordinal)).ToList();

        var subjects = unselectable.SelectMany(r => Subjects(r.Selector)).ToList();

        Assert.True(subjects.Count >= 30, $"{subjects.Count} parts made unselectable");
        Assert.All(subjects, subject => Assert.DoesNotContain(Content, marker => subject.Contains(marker, StringComparison.Ordinal)));
        Assert.All(subjects, subject => Assert.DoesNotContain(AttributeFree().Replace(subject, ""), ContentTags));
    }

    [Fact]
    public void A_link_keeps_its_long_press_preview()
    {
        var callouts = NativeFeel().Where(r => r.Body.Contains("touch-callout", StringComparison.Ordinal)).ToList();

        Assert.Single(callouts);
        Assert.Contains(":not(a[href])", callouts[0].Selector, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_safe_area_inset_is_the_most_of_it_and_the_kits_own_padding_so_it_is_inert_without_a_notch()
    {
        var css = UiStylesheet.Css;

        var insets = Regex.Matches(css, @"env\(safe-area-inset-[a-z]+\)").Count;
        var held = Regex.Matches(css, @"max\([^,()]+,\s*env\(safe-area-inset-[a-z]+\)\)").Count;

        Assert.True(insets >= 12, $"{insets} safe-area insets in the sheet");
        Assert.Equal(insets, held);
    }

    [Theory]
    [InlineData("[data-ui-header]", "padding-left:max(1.5rem,env(safe-area-inset-left))")]
    [InlineData("[data-ui-sidebar]", "max(1rem,env(safe-area-inset-top))")]
    [InlineData("[data-ui-modal]>dialog[data-ui-flyout]", "max(2rem,env(safe-area-inset-bottom))")]
    [InlineData("[data-ui-toast],[data-ui-toast-group]", "inset:max(0px,env(safe-area-inset-top))")]
    [InlineData("[data-ui-modal]>dialog,[data-ui-command-items],[data-ui-menu],[data-ui-popover]", "overscroll-behavior:contain")]
    [InlineData("[data-ui-avatar] img,[data-ui-brand] img,[data-ui-sidebar-brand] img,[data-ui-flag] img", "-webkit-user-drag:none")]
    public void The_parts_that_meet_an_edge_or_scroll_inside_an_overlay_say_so(string selector, string declaration)
    {
        var rule = NativeFeel().First(r => r.Selector == $":where({selector})");

        Assert.Contains(declaration, rule.Body.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static List<(string Selector, string Body)> NativeFeel() =>
        UiStylesheetTests.Rules(UiStylesheet.Css)
            .Select(r => (r.Selector, Body: r.Body.Replace(" ", "", StringComparison.Ordinal)))
            .Where(r => r.Selector.Contains("data-ui-", StringComparison.Ordinal)
                        && Declarations.Any(d => r.Body.Contains(d, StringComparison.Ordinal)))
            .ToList();

    // What each alternative of a `:where(…)` selects: the compound after its last combinator.
    private static IEnumerable<string> Subjects(string selector)
    {
        var inner = selector[":where(".Length..^1];
        var depth = 0;
        var start = 0;
        for (var i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || (inner[i] == ',' && depth == 0))
            {
                var alternative = inner[start..i];
                var last = Math.Max(alternative.LastIndexOfAny([' ', '>', '+', '~']), -1);
                yield return depth == 0 && last >= 0 && !alternative[(last + 1)..].Contains(')', StringComparison.Ordinal)
                    ? alternative[(last + 1)..]
                    : alternative;
                start = i + 1;
                continue;
            }

            depth += inner[i] switch { '(' => 1, ')' => -1, _ => 0 };
        }
    }

    // Where the parenthesis opened by the leading `:where(` closes.
    private static int Closing(string selector)
    {
        var depth = 0;
        for (var i = 0; i < selector.Length; i++)
        {
            depth += selector[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0 && selector[i] == ')')
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"\[[^\]]*\]")]
    private static partial Regex AttributeFree();
}
