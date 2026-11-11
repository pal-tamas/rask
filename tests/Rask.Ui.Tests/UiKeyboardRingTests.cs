namespace Rask.UiTests;

/// <summary>
///     THE KEYBOARD RING (owner, 2026-11-10): every element the keyboard focuses is ringed in the app's accent, 2px laid
///     inside its own box so no frame around it clips it — not the browser's blue ring Flux leaves, which TA's table cut
///     in half round its ⋯. Read from the COMPILED sheet and the markup.
/// </summary>
public sealed class UiKeyboardRingTests
{
    private const string Band = "focus-visible:shadow-[inset_0_0_0_4px_var(--color-accent-foreground)]";

    [Fact]
    public void Keyboard_focus_on_anything_draws_the_accent_inside_its_own_box()
    {
        var rules = Rules();

        var ring = Assert.Single(rules, r => r.Selector == ":where(:focus-visible)");

        Assert.Contains("outline:2pxsolidvar(--color-accent)", ring.Body, StringComparison.Ordinal);
        Assert.Contains("outline-offset:-2px", ring.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ring_is_at_no_weight_in_the_kits_own_layer_so_any_utility_wins()
    {
        var css = UiStylesheet.Css;

        var at = System.Text.RegularExpressions.Regex.Match(css, @"(?<=[{};]\s*):where\(:focus-visible\)\s*\{").Index;
        var enclosing = Enclosing(css, at);

        Assert.True(at > 0);
        Assert.Equal("@layer rask", enclosing);
    }

    // The prelude of the block the text at `at` sits in: walk back to the first `{` no `}` closes.
    private static string Enclosing(string css, int at)
    {
        var depth = 0;
        for (var i = at - 1; i >= 0; i--)
        {
            depth += css[i] switch { '}' => 1, '{' => -1, _ => 0 };
            if (depth < 0)
            {
                var start = css.LastIndexOfAny(['{', '}', ';'], i - 1) + 1;
                return css[start..i].Trim();
            }
        }

        return "";
    }

    [Fact]
    public void No_part_of_the_kit_draws_the_browsers_blue_ring()
    {
        var css = UiStylesheet.Css;

        var blue = css.Contains("-webkit-focus-ring-color", StringComparison.Ordinal);

        Assert.False(blue);
    }

    [Fact]
    public void A_box_radio_or_switch_is_ringed_in_the_accent_round_what_the_reader_sees()
    {
        var html = Ui.Checkbox.Key("a").Name("a").ToHtml().AsText() + Ui.Radio.Key("b").Value("b").ToHtml().AsText()
            + Ui.Switch.Value(false).Key("c").Name("c").ToHtml().AsText();

        var rings = html.Split(UiFocusLook.Ring).Length - 1;

        Assert.Equal(3, rings);
        Assert.Contains(Rules(), r => r.Selector.Contains("focus-visible", StringComparison.Ordinal)
                                      && r.Body.Contains("outline:2pxsolidvar(--color-accent)", StringComparison.Ordinal)
                                      && r.Selector.Contains(":has(", StringComparison.Ordinal));
    }

    [Fact]
    public void A_box_radio_or_switch_is_ringed_inside_so_a_tables_first_column_cuts_none_of_it()
    {
        var box = Ui.Checkbox.Key("a").Name("a").ToHtml().AsText();
        var on = Ui.Switch.Value(true).Key("c").Name("c").ToHtml().AsText();

        var outside = box.Contains(" outline-offset-2", StringComparison.Ordinal) || on.Contains(" outline-offset-2", StringComparison.Ordinal);

        Assert.False(outside);
        Assert.Contains(UiFocusLook.Inside, box, StringComparison.Ordinal);
        Assert.Contains("group-has-checked/option:group-has-[:focus-visible]/option:shadow-[inset_0_0_0_4px_var(--color-accent-foreground)]", box, StringComparison.Ordinal);
        Assert.Contains(UiFocusLook.OnAccentChecked, on, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sort_button_at_the_tables_edge_hangs_nowhere_past_the_scroll_frame()
    {
        var rules = Rules();

        var first = Assert.Single(rules, r => r.Selector == "[data-ui-column]:first-child [data-ui-table-sortable]");
        var last = Assert.Single(rules, r => r.Selector == "[data-ui-column]:last-child [data-ui-table-sortable]");

        Assert.Contains("margin-inline-start:0", first.Body, StringComparison.Ordinal);
        Assert.Matches("padding-inline-start:0?\\.25rem", first.Body);
        Assert.Contains("margin-inline-end:0", last.Body, StringComparison.Ordinal);
        Assert.Matches("padding-inline-end:0?\\.25rem", last.Body);
    }

    [Fact]
    public void A_last_heading_aligned_to_the_end_keeps_its_words_over_its_numbers_and_its_ring_inside()
    {
        var rules = Rules();

        var end = Assert.Single(rules, r => r.Selector.StartsWith("[data-ui-column]:last-child.group", StringComparison.Ordinal)
            && r.Selector.EndsWith("end-align [data-ui-table-sortable]", StringComparison.Ordinal));
        var ring = Assert.Single(rules, r => r.Selector == ":where(:focus-visible)");

        Assert.Contains("padding-inline-end:0", end.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("margin", end.Body, StringComparison.Ordinal);
        Assert.Contains("outline-offset:-2px", ring.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_primary_button_lays_a_band_of_the_accents_foreground_inside_the_ring()
    {
        var primary = Ui.Button.Key("save").Primary["Save"].ToHtml().AsText();
        var outline = Ui.Button.Key("cancel")["Cancel"].ToHtml().AsText();

        Assert.Contains(Band, primary, StringComparison.Ordinal);
        Assert.DoesNotContain(Band, outline, StringComparison.Ordinal);
        Assert.Contains(Rules(), r => r.Body.Contains("inset0004pxvar(--tw-shadow-color,var(--color-accent-foreground))", StringComparison.Ordinal));
    }

    [Fact]
    public void A_chosen_pill_tab_lays_the_band_inside_the_ring_and_an_underlined_one_does_not()
    {
        var pills = Ui.Tabs.Pills.Key("p")[Ui.Tab.Key("a").Name("a")["A"]].ToHtml().AsText();
        var underlined = Ui.Tabs.Key("u")[Ui.Tab.Key("a").Name("a")["A"]].ToHtml().AsText();

        Assert.Contains("data-selected:" + Band, pills, StringComparison.Ordinal);
        Assert.DoesNotContain(Band, underlined, StringComparison.Ordinal);
    }

    [Fact]
    public void A_typed_dates_part_is_lit_inside_itself_so_the_row_of_parts_cuts_none_of_it()
    {
        var rules = Rules();

        var part = Assert.Single(rules, r => r.Selector.Contains("[data-ui-month-input]", StringComparison.Ordinal)
                                             && r.Body.Contains("outline:1pxsolidvar(--color-accent)", StringComparison.Ordinal));

        Assert.Contains("outline-offset:-1px", part.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_inputs_choose_button_is_ringed_inside_while_its_hidden_input_has_the_keyboard()
    {
        var html = Ui.Input.Of<string>().Type(Rask.Core.InputType.File).Label("Logo").ToHtml().AsText();

        var button = html[html.IndexOf("data-ui-button", StringComparison.Ordinal)..];

        Assert.Contains(UiFocusLook.ForHiddenInput, html, StringComparison.Ordinal);
        Assert.Contains("class=\"sr-only\"", html, StringComparison.Ordinal);
        Assert.Contains("Choose file", button, StringComparison.Ordinal);
        Assert.Contains(Rules(), r => r.Selector.Contains("input:focus-visible~", StringComparison.Ordinal)
                                      && r.Body.Contains("outline:2pxsolidvar(--color-accent)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_browser_date_field_stays_ringed_while_its_calendar_button_has_the_keyboard()
    {
        var rules = Rules();

        var field = Assert.Single(rules, r => r.Body.Contains("box-shadow:inset0001pxvar(--color-accent)", StringComparison.Ordinal));

        Assert.Contains("[type=date]", field.Selector.Replace("\"", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("):focus-within,", field.Selector.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    private static List<(string Selector, string Body)> Rules() =>
        UiStylesheetTests.Rules(UiStylesheet.Css)
            .Select(r => (r.Selector, Body: r.Body.Replace(" ", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal)))
            .ToList();
}
