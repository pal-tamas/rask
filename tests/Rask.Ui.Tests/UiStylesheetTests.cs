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

    [Fact]
    public void Every_rule_is_inside_a_layer()
    {
        // Unlayered CSS beats layered CSS in every browser, whatever the source order. Tailwind puts an
        // application's own utilities in `@layer utilities`, so an unlayered rule shipped by a library
        // silently outranks every utility that application wrote — and the symptom is not a colour or a
        // margin, it is a layout that quietly stops responding. This sheet shipped unlayered once: the
        // kit's `.hidden` beat the showcase's `.md:flex`, pinning its sidebar to display:none at every
        // width while the rest of the page looked entirely normal. Thirteen browser tests found it, and
        // nothing cheaper could have.
        // Comments first: the sheet opens with Tailwind's banner, and a comment sitting before an
        // at-rule would otherwise be read as part of its selector.
        var css = Regex.Replace(UiStylesheet.Css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var depth = 0;

        for (var i = 0; i < css.Length; i++)
        {
            if (css[i] == '}')
            {
                depth--;
                continue;
            }

            if (css[i] != '{')
            {
                continue;
            }

            if (depth++ != 0)
            {
                continue;
            }

            // The selector this block belongs to: everything back to the previous block boundary. At the
            // top level it has to be an at-rule — @layer, @property, @media, @supports — and never a
            // bare selector, which is what an unlayered rule looks like.
            var start = css.LastIndexOfAny(['}', ';'], i) + 1;
            var selector = css[start..i].Trim();

            Assert.True(
                selector.StartsWith('@'),
                $"Unlayered rule at the top level of the kit's stylesheet: '{Head(selector)}'. It would "
                + "outrank every layered utility in any application that inlines this sheet.");
        }
    }

    [Fact]
    public void The_mockup_prompt_keeps_its_gap_from_the_command()
    {
        // #1032. daisyUI gives the prompt pseudo-element `margin-right: 2ch` in its base rule and then
        // REPLACES that declaration block in the nested rule that supplies the content, so the gap is
        // lost and a terminal renders `$curl` with the prompt flush against the command.
        //
        // WIDTH, and this test exists mostly to say why it is not margin. The correction went in three
        // times as `margin-right: 2ch` - the exact declaration daisyUI's own base rule sets and its
        // nested rule drops - at two different specificities and in two different layers, and the
        // browser computed 0px every time. A consuming app's Tailwind preflight resets margin and
        // padding on ::before from its own <link>, and layers do not merge across separate sheets, so
        // nothing in this sheet can win that. Widening the box daisyUI already right-aligns the prompt
        // inside uses a property the reset does not touch.
        //
        // It is a UTILITY on the element now rather than a rule in this stylesheet - the kit writes no
        // custom CSS - so this half asserts the sheet and UiMockupTests asserts the markup that names
        // it. What has to stay true did not change: the gap comes from width, and a margin here reads
        // as a fix and is not one.
        //
        // A class the markup names but Tailwind never emitted is the silent half of this failure - the
        // markup would look correct and the gap would still be gone - so the emitted declaration is
        // what is checked, not the class name.
        Assert.Contains("width:calc(2rem + 2ch)", UiStylesheet.Css, StringComparison.Ordinal);

        // `margin-right:2ch` IS in this sheet - it is daisyUI's own base rule, the one whose nested
        // rule then drops it - so its presence is not the thing to assert against. What must not come
        // back is a CORRECTION written as a margin, which is why the check above is on the width.
    }

    [Fact]
    public void The_mockup_prompt_gap_does_not_blank_the_prompt()
    {
        // The hazard the utility brought with it, and the reason it is safe.
        //
        // Tailwind's `before:` variant does not emit width alone. It emits
        //
        //     .before\:w-\[calc\(2rem\+2ch\)\]:before { content: var(--tw-content); width: ... }
        //
        // because a pseudo-element with no `content` does not render at all, so the variant always
        // supplies one. That declaration lands in the UTILITIES layer, which is last, so it beats
        // daisyUI's own `content` on the same pseudo-element. A utility asked for the gap and would
        // have taken the `$` away with it - the prompt would vanish and the gap would look perfect.
        //
        // It does not, because both sides are Tailwind: daisyUI sets `--tw-content: attr(data-prefix)`
        // on this very element, and the utility's `content: var(--tw-content)` reads it back. The
        // winning declaration resolves to exactly what it overrode.
        //
        // That is a coincidence of daisyUI being built on Tailwind, not a guarantee, and it is invisible
        // in both the markup and the computed width. So it is pinned here: if daisyUI ever supplies the
        // prompt with a literal `content` instead of the variable, this fails rather than the terminal
        // silently losing its prompts.
        var prefixRule = Assert.Single(
            System.Text.RegularExpressions.Regex.Matches(
                    UiStylesheet.Css, @"\.mockup-code pre\[data-prefix\]:before\{([^}]*)\}")
                .Select(m => m.Groups[1].Value)
                .ToList());

        Assert.Contains("--tw-content:attr(data-prefix)", prefixRule, StringComparison.Ordinal);
        Assert.Contains("content:var(--tw-content)", prefixRule, StringComparison.Ordinal);

        // And the utility takes the same route rather than hard-coding a content of its own.
        Assert.Contains(
            "content:var(--tw-content);width:calc(2rem + 2ch)", UiStylesheet.Css, StringComparison.Ordinal);
    }

    private static string Head(string s) => s.Length <= 80 ? s : s[..80] + "…";

}
