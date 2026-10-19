using System.Text.RegularExpressions;

namespace Rask.UiTests;

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
    public void The_scroll_lock_at_the_document_root_applies_only_under_an_open_kit_dialog()
    {
        // The kit's rules at :root, which a library stylesheet would otherwise have no business writing. They are
        // safe only while every selector in them requires an OPEN kit overlay to be in the document: a modal, or
        // a menu, behind which Flux's page does not scroll either — or the color picker's dialog, whose own rule
        // is inside a media query: it is Flux's sheet below `sm`, and only there is the page held.
        var locks = Rules(UiStylesheet.Css)
            .Where(r => r.Selector.Contains(":root:has(", StringComparison.Ordinal)
                        && r.Body.Contains("overflow", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(locks);
        foreach (var rule in locks)
        {
            var inner = rule.Selector[(rule.Selector.IndexOf(":has(", StringComparison.Ordinal) + 5)..];
            foreach (var alternative in inner.TrimEnd(')').Split(','))
            {
                Assert.Matches("modal|data-ui-menu|data-ui-navmenu|data-ui-color-picker", alternative);
                Assert.Matches(@"\[open\]|:popover-open|\.modal-open", alternative);
            }
        }
    }

    [Fact]
    public void No_reset_or_document_rule_reaches_a_page_without_the_console_frame()
    {
        // The kit ships utilities and components, never a reset for an application: an app owns its own
        // document, and a second reset arriving from a library restyles pages that never asked for it. The
        // one reset it does carry is the console frame's, and every rule of it names `[data-rask-ui-document]` — the attribute
        // only a kit-only document writes — so a document that never renders the frame matches none of it.
        foreach (var rule in Rules(UiStylesheet.Css))
        {
            var body = rule.Body.Replace(" ", "", StringComparison.Ordinal);
            var touchesDocument = Regex.IsMatch(rule.Selector, @"(^|,)\s*(html|body)\b");
            var isReset = body.Contains("box-sizing:border-box", StringComparison.Ordinal)
                          && body.Contains("margin:0", StringComparison.Ordinal);

            if (touchesDocument || isReset)
            {
                Assert.Contains("data-rask-ui-document", rule.Selector, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_console_frame_brings_its_own_reset_and_drops_the_body_margin()
    {
        // Rask.Dashboard owns a whole document and draws it with nothing but this kit, so the frame's reset
        // is the only one that page gets. Absent, the console renders with the browser's default margins
        // on every heading, list and paragraph, and an 8px white border round the frame.
        var rules = Rules(UiStylesheet.Css)
            .Select(r => (r.Selector, Body: r.Body.Replace(" ", "", StringComparison.Ordinal)))
            .ToList();

        Assert.Contains(rules, r => r.Selector.Contains("data-rask-ui-document", StringComparison.Ordinal)
                                    && r.Body.Contains("box-sizing:border-box", StringComparison.Ordinal));
        Assert.Contains(rules, r => r.Selector.EndsWith(" body", StringComparison.Ordinal)
                                    && r.Selector.Contains("data-rask-ui-document", StringComparison.Ordinal)
                                    && r.Body.Contains("margin:0", StringComparison.Ordinal));
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
        // A selector starts at the top of the sheet or after a brace or a semicolon — never after whitespace.
        // Whitespace IS a selector's descendant combinator: starting there read `[data-rask-ui-document] *)::file-selector-button`
        // as the selector `*)::file-selector-button`, dropping the very scope the assertions look for.
        //
        // A LOOKBEHIND, not a consumed character. Minified CSS puts rules edge to edge (`…}body:has(…){…}`), and
        // a boundary the previous match's closing brace had already consumed could not start the next rule — so
        // every rule sitting directly after another was skipped, and these assertions read half the sheet.
        foreach (Match m in Regex.Matches(css, @"(?:^|(?<=[{};]))([^{}@;]+)\{([^{}]*)\}"))
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
