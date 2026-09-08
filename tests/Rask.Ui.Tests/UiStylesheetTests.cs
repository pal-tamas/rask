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
        // WIDTH, and this test exists mostly to say why it is not margin. The correcting rule went in
        // three times as `margin-right: 2ch` — the exact declaration daisyUI's own base rule sets and its
        // nested rule drops — at two different specificities and in two different layers, and the browser
        // computed 0px every time. A consuming app's Tailwind preflight resets margin and padding on
        // ::before from its own <link>, and layers do not merge across separate sheets, so nothing layered
        // in this sheet can win that. Widening the box daisyUI already right-aligns the prompt inside
        // uses a property the reset does not touch.
        //
        // So the assertion is: the correction sets width, and it does NOT set margin — because a margin
        // here reads as a fix and is not one.
        var correction = Assert.Single(
            LayersOf(UiStylesheet.Css, ".mockup-code pre[data-prefix]:before")
                .Where(r => r.Body.Contains("width", StringComparison.Ordinal)
                            && !r.Body.Contains("content", StringComparison.Ordinal))
                .ToList());

        Assert.Contains("2ch", correction.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("margin", correction.Body, StringComparison.Ordinal);

        // Outside daisyUI's own sublayer, so it is a correction rather than part of what it corrects.
        var layer = Assert.Single(correction.Layers);
        Assert.DoesNotContain("daisyui", layer, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Every rule in <paramref name="css" /> whose selector is <paramref name="selector" />, with the
    ///     stack of at-rules enclosing it. The stack is what decides which of two identical selectors wins.
    /// </summary>
    private static IEnumerable<(IReadOnlyList<string> Layers, string Body)> LayersOf(string css, string selector)
    {
        var stripped = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var stack = new List<string>();
        var found = new List<(IReadOnlyList<string>, string)>();

        foreach (Match m in Regex.Matches(stripped, @"@[a-zA-Z-]+[^{;]*[{;]|[^{}@;]+\{|\}"))
        {
            var token = m.Value.Trim();

            if (token == "}")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            if (token.EndsWith(';'))
            {
                continue;
            }

            var head = token[..^1].Trim();

            if (head.StartsWith('@'))
            {
                stack.Add(head);
                continue;
            }

            if (head == selector)
            {
                var end = stripped.IndexOf('}', m.Index + m.Length);
                found.Add((stack.ToArray(), end < 0 ? "" : stripped[(m.Index + m.Length)..end]));
            }

            // A plain selector opens a block that has to be closed before the stack is read again.
            stack.Add(head);
        }

        return found;
    }

    private static string Head(string s) => s.Length <= 80 ? s : s[..80] + "…";

}
