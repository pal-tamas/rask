using System.Text.RegularExpressions;

namespace Rask.UiTests;

/// <summary>
///     The hand over what can be pressed — an owner-approved departure from Flux (2026-11-02). One rule in the kit's
///     sheet gives it to a document's buttons, pressable roles, toggling labels and clickable inputs, the kit's and the
///     application's alike; a second gives the arrow back to whatever of those cannot be pressed now. Read from the
///     COMPILED sheet, where Tailwind has merged and minified the layers.
/// </summary>
public sealed class UiPointerCursorTests
{
    private static readonly string[] Pressable =
    [
        "button", "[role=button]", "[role=menuitem]", "[role=menuitemcheckbox]", "[role=menuitemradio]", "[role=option]",
        "[role=tab]", "[role=checkbox]", "[role=radio]", "[role=switch]", "summary", "select", "label[for]",
        "label:has(input:is([type=checkbox],[type=radio]):not(:disabled))",
        "input:is([type=checkbox],[type=radio],[type=file],[type=range],[type=color],[type=submit],[type=button],[type=reset])",
    ];

    private static readonly string[] NotNow =
    [
        ":disabled", "[aria-disabled=true]", "[data-disabled]", "[readonly]", "[aria-readonly=true]", "[data-loading]",
        "[aria-busy=true]", "[inert]", "[inert] *",
    ];

    [Fact]
    public void The_hand_is_one_zero_specificity_rule_in_the_kit_layer_over_every_pressable_thing()
    {
        var rules = Rules(UiStylesheet.Css);

        var hand = Assert.Single(rules, r => r.Body == "cursor:pointer" && r.Selector.StartsWith(":where(button,", StringComparison.Ordinal));

        Assert.Equal("rask", hand.Layer);
        Assert.Equal([hand.Selector], Alternatives(hand.Selector));
        Assert.EndsWith(")", hand.Selector, StringComparison.Ordinal);
        Assert.All(Pressable, part => Assert.Contains(part, hand.Selector, StringComparison.Ordinal));
        // By what an element IS, never by a handler: a plain <div> with a click handler gets nothing.
        Assert.DoesNotContain("data-rask-on", hand.Selector, StringComparison.Ordinal);
    }

    [Fact]
    public void What_cannot_be_pressed_now_gets_the_arrow_back_after_the_hand_and_at_no_more_weight()
    {
        var rules = Rules(UiStylesheet.Css);

        var hand = rules.FindIndex(r => r.Body == "cursor:pointer" && r.Selector.StartsWith(":where(button,", StringComparison.Ordinal));
        var arrow = rules.FindIndex(r => r.Body == "cursor:default" && r.Selector.StartsWith(":where(button,", StringComparison.Ordinal));

        Assert.True(hand >= 0 && arrow > hand, "The arrow's rule must come after the hand's: both weigh nothing, so the later one wins.");
        Assert.Equal("rask", rules[arrow].Layer);
        Assert.All(Alternatives(rules[arrow].Selector), part => Assert.Matches(@"^(:where\([^{]*\))+$", part));
        Assert.All(NotNow, state => Assert.Contains(state, rules[arrow].Selector, StringComparison.Ordinal));
        // A label round a disabled box is not matched by the hand at all, and keeps the browser's `auto`, as Flux's does.
        Assert.Contains("label:has(input:is([type=checkbox],[type=radio]):not(:disabled))", rules[hand].Selector, StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_field_keeps_its_caret_because_no_rule_hands_a_text_input_the_pointer()
    {
        var rules = Rules(UiStylesheet.Css);

        var hand = rules.Single(r => r.Body == "cursor:pointer" && r.Selector.StartsWith(":where(button,", StringComparison.Ordinal));

        // A bare `input`, or a label around any input, would give a text field the hand: cursor is inherited.
        Assert.DoesNotMatch(@"[(,]input[,)]", hand.Selector);
        Assert.DoesNotContain("label:has(input:not(", hand.Selector, StringComparison.Ordinal);
        Assert.DoesNotContain("[type=text]", hand.Selector, StringComparison.Ordinal);
    }

    // Every style rule of the sheet with the outermost @layer it sits in, in source order.
    private static List<(string Selector, string Body, string? Layer)> Rules(string css)
    {
        css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
        var rules = new List<(string, string, string?)>();
        var preludes = new Stack<string>();
        var start = 0;
        for (var i = 0; i < css.Length; i++)
        {
            switch (css[i])
            {
                case ';' when !InsideRule(preludes):
                    start = i + 1;
                    break;
                case '{':
                    preludes.Push(css[start..i].Trim());
                    start = i + 1;
                    break;
                case '}':
                    var prelude = preludes.Pop();
                    if (!prelude.StartsWith('@'))
                    {
                        rules.Add((prelude, css[start..i].Trim().TrimEnd(';'), Layer(preludes)));
                    }

                    start = i + 1;
                    break;
            }
        }

        return rules;
    }

    private static bool InsideRule(Stack<string> preludes) => preludes.Count > 0 && !preludes.Peek().StartsWith('@');

    private static string? Layer(Stack<string> preludes) =>
        preludes.LastOrDefault(p => p.StartsWith("@layer ", StringComparison.Ordinal))?["@layer ".Length..].Trim();

    // A selector list's alternatives: its commas outside any parenthesis.
    private static List<string> Alternatives(string selector)
    {
        var parts = new List<string>();
        var (depth, start) = (0, 0);
        for (var i = 0; i < selector.Length; i++)
        {
            depth += selector[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (selector[i] == ',' && depth == 0)
            {
                parts.Add(selector[start..i]);
                start = i + 1;
            }
        }

        parts.Add(selector[start..]);
        return parts;
    }
}
