namespace Rask.Ui.Tests.Components;

/// <summary>
///     The multi-select, in both of its modes.
/// </summary>
/// <remarks>
///     The chain opens on <c>Value</c> or <c>Bind</c> over a COLLECTION, which is what pins the element
///     type and the mode together; <c>Label</c> and <c>Options</c> follow in either order. Binding
///     itself is covered in <see cref="UiFormBindingTests" /> — what is asserted here is the markup.
/// </remarks>
public partial class UiMultiSelectTests : global::Rask.Core.RaskMarkup
{
    private static readonly (string Value, string Text)[] Packages =
    [
        ("core", "Rask.Core"), ("ui", "Rask.Ui"), ("cli", "Rask.Cli"),
        ("blazor", "Rask.Blazor"), ("ext", "Rask.External")
    ];

    [Fact]
    public void The_default_is_the_platforms_own_multi_select()
    {
        // It works with no script, renders complete on a prerendered page, and posts under its own name
        // — the same reasons UiSelect defaults to native.
        var html = Native(["core"]);

        Assert.Contains("<select", html);
        Assert.Contains("multiple", html);
        Assert.DoesNotContain("role=\"listbox\"", html);
    }

    [Fact]
    public void The_native_mode_marks_every_chosen_option_not_just_the_first()
    {
        // The bug this guards: a multi-select whose markup marks one option converges the control on a
        // single answer the moment the browser echoes it back.
        var html = Native(["core", "cli"]);

        Assert.Equal(2, Occurrences(html, "selected"));
    }

    [Fact]
    public void Turning_it_off_draws_a_combobox_over_a_listbox()
    {
        var html = Custom(["core"]);

        Assert.DoesNotContain("<select", html);
        Assert.Contains("role=\"combobox\"", html);
        Assert.Contains("role=\"listbox\"", html);
        Assert.Contains("role=\"option\"", html);
    }

    [Fact]
    public void The_box_around_the_chips_announces_nothing_of_its_own()
    {
        // The combobox is the BUTTON inside the box, and there is exactly one of it. The box is a styled
        // container: a second role here would announce two controls where there is one, and the
        // aria-hidden that would suppress it must never land on an element holding the chips' own
        // focusable remove buttons.
        var html = Custom(["core", "ui"]);

        Assert.Equal(1, Occurrences(html, "role=\"combobox\""));
        Assert.DoesNotContain("aria-hidden=\"true\"><span class=\"badge", html);
    }

    [Fact]
    public void The_drawn_list_says_it_takes_more_than_one_answer()
    {
        // Without aria-multiselectable a reader meets a listbox whose options each carry aria-selected
        // and has no way to learn that a second one is allowed.
        Assert.Contains("aria-multiselectable=\"true\"", Custom(["core"]));
    }

    [Fact]
    public void Several_options_are_selected_at_once()
    {
        var html = Custom(["core", "cli"]);

        Assert.Equal(2, Occurrences(html, "aria-selected=\"true\""));
        Assert.Equal(2, Occurrences(html, "menu-active"));
    }

    [Fact]
    public void Choosing_an_option_does_not_close_the_list()
    {
        // The single-select closes declaratively, with popovertargetaction="hide" on every option. Here
        // that attribute's ABSENCE is the feature: picking three answers must not mean opening the list
        // three times.
        Assert.DoesNotContain("popovertargetaction", Custom(["core"]));
    }

    [Fact]
    public void The_box_shows_the_chosen_answers_as_chips()
    {
        var html = Custom(["core", "ui"]);

        Assert.Contains("badge", html);
        Assert.Contains("Rask.Core", html);
        Assert.Contains("Rask.Ui", html);
    }

    [Fact]
    public void Past_the_chip_limit_the_rest_become_a_count()
    {
        // Three chips by default, so five answers show three and say so about the other two — the box
        // cannot grow without bound and shift everything under it.
        var html = Custom(["core", "ui", "cli", "blazor", "ext"]);

        // "2 more", not "+2 more": the leading plus arrives HTML-encoded, because the count is a Text
        // node and Text encodes. Asserting the bare number keeps the test about the arithmetic.
        Assert.Equal(3, Occurrences(html, "badge-sm"));
        Assert.Contains("2 more", html);
    }

    [Fact]
    public void The_box_can_still_be_opened_when_every_answer_fitted_into_chips()
    {
        // The regression this pins is invisible in markup and only a browser found it. With fewer
        // answers than the chip limit there is no overflow to report, so the invoker says NOTHING —
        // and an empty flex child collapses to zero height, leaving no region to click to open the
        // list and a combobox reported as not visible. The caret is daisyUI's background image on the
        // BOX, so it looks clickable while the button behind it has no area at all.
        var html = Custom(["core", "ui"]);

        Assert.Contains("min-h-6", html);
    }

    [Fact]
    public void Chips_zero_collapses_to_a_count_alone()
    {
        // For a box that must keep one width whatever is picked.
        var html = UiMultiSelect.Value<string>(["core", "ui"]).Options(Packages).Label("Packages")
            .Native(false).Chips(0).ToHtml();

        Assert.Contains("2 selected", html);
        Assert.DoesNotContain("badge", html);
    }

    [Fact]
    public void An_empty_selection_shows_the_placeholder_and_no_chips()
    {
        var html = UiMultiSelect.Value<string>([]).Options(Packages).Label("Packages")
            .Native(false).Placeholder("Choose packages").ToHtml();

        Assert.Contains("Choose packages", html);
        Assert.DoesNotContain("badge", html);
    }

    [Fact]
    public void The_placeholder_never_becomes_an_option()
    {
        // Unlike the single-select's. A "choose one" row in a list you may choose several from is an
        // answer that contradicts its own question.
        var html = UiMultiSelect.Value<string>([]).Options(Packages).Label("Packages")
            .Placeholder("Choose packages").ToHtml();

        Assert.DoesNotContain("<option value=\"\"", html);
    }

    [Fact]
    public void The_search_box_appears_only_when_a_filter_was_supplied()
    {
        // Supplying the predicate is what adds the box: the control never assumes the shape of your
        // data, so it cannot decide on its own what "matches" means.
        Assert.DoesNotContain("Search…", Custom(["core"]));
    }

    [Fact]
    public void The_select_all_row_appears_only_when_asked_for()
    {
        Assert.DoesNotContain("Select all", Custom(["core"]));
        Assert.Contains("Select all",
            UiMultiSelect.Value<string>(["core"]).Options(Packages).Label("Packages")
                .Native(false).SelectAll(true).ToHtml());
    }

    [Fact]
    public void Select_all_becomes_clear_all_once_everything_is_in()
    {
        var html = UiMultiSelect.Value<string>(["core", "ui", "cli", "blazor", "ext"])
            .Options(Packages).Label("Packages").Native(false).SelectAll(true).ToHtml();

        Assert.Contains("Clear all", html);
        Assert.DoesNotContain(">Select all<", html);
    }

    [Fact]
    public void Select_all_ignores_the_options_it_may_not_choose()
    {
        // Everything selectable is already in, so the bulk action has nothing left to add even though
        // one option is unpicked — it must not offer to select a disabled row.
        var html = UiMultiSelect.Value<string>(["core", "ui", "cli", "ext"])
            .Options(Packages).Label("Packages").Native(false).SelectAll(true)
            .OptionDisabled(v => v == "blazor").ToHtml();

        Assert.Contains("Clear all", html);
    }

    [Fact]
    public void A_disabled_option_says_so_and_is_unreachable()
    {
        var html = UiMultiSelect.Value<string>([]).Options(Packages).Label("Packages")
            .Native(false).OptionDisabled(v => v == "blazor").ToHtml();

        Assert.Contains("aria-disabled=\"true\"", html);
        Assert.Contains("menu-disabled", html);
    }

    [Fact]
    public void An_enabled_option_writes_no_aria_disabled_at_all()
    {
        // Not aria-disabled="false" and above all not a valueless one, which reads as "true" and would
        // mark every option in the list unavailable.
        Assert.DoesNotContain("aria-disabled", Custom(["core"]));
    }

    [Fact]
    public void Grouping_puts_headers_in_the_list()
    {
        var html = UiMultiSelect.Value<string>([]).Options(Packages).Label("Packages")
            .Native(false).OptionGroup(v => v is "core" or "ui" ? "Rendering" : "Tooling").ToHtml();

        Assert.Contains("menu-title", html);
        Assert.Contains("Rendering", html);
        Assert.Contains("Tooling", html);
    }

    [Fact]
    public void The_hidden_inputs_appear_only_when_the_field_is_named()
    {
        // A listbox of buttons submits nothing, so without them a control inside a plain <form> would
        // silently drop its field.
        Assert.DoesNotContain("type=\"hidden\"", Custom(["core", "ui"]));

        var html = UiMultiSelect.Value<string>(["core", "ui"]).Options(Packages).Label("Packages")
            .Native(false).Name("packages").ToHtml();

        // One per answer, all sharing the name — byte for byte what <select multiple> posts, so a server
        // that already reads the native control reads this one unchanged.
        Assert.Equal(2, Occurrences(html, "type=\"hidden\""));
        Assert.Equal(2, Occurrences(html, "name=\"packages\""));
    }

    [Fact]
    public void An_option_template_draws_the_rows_and_implies_the_drawn_list()
    {
        // An <option> holds text and nothing else, so there is nowhere in the platform's control for
        // markup to go — supplying a template is therefore a choice of mode as well.
        var html = UiMultiSelect.Value<string>(["core"]).Options(Packages).Label("Packages")
            .OptionTemplate(v => Span.Class("badge-dot")[v]).ToHtml();

        Assert.DoesNotContain("<select", html);
        Assert.Contains("badge-dot", html);
    }

    [Fact]
    public void A_chip_template_draws_the_box_while_the_rows_keep_their_words()
    {
        var html = UiMultiSelect.Value<string>(["core"]).Options(Packages).Label("Packages")
            .Native(false).ChipTemplate(v => Span.Class("chip-mark")[v]).ToHtml();

        Assert.Contains("chip-mark", html);
        Assert.Contains("Rask.Core", html);
    }

    [Fact]
    public void The_accessible_name_is_given_directly_in_both_modes()
    {
        // role="combobox" is not a labelable element, so a <label for> would bind to nothing.
        Assert.Contains("aria-label=\"Packages\"", Native(["core"]));
        Assert.Contains("aria-label=\"Packages\"", Custom(["core"]));
    }

    [Fact]
    public void An_error_tone_says_so_to_a_screen_reader_in_both_modes()
    {
        // A field that is visibly red and says nothing is half a message.
        Assert.Contains("aria-invalid=\"true\"",
            UiMultiSelect.Value<string>([]).Options(Packages).Label("Packages")
                .Tone(UiTone.Error).ToHtml());
        Assert.Contains("aria-invalid=\"true\"",
            UiMultiSelect.Value<string>([]).Options(Packages).Label("Packages")
                .Native(false).Tone(UiTone.Error).ToHtml());
    }

    [Fact]
    public void The_closed_list_reports_itself_closed()
    {
        var html = Custom(["core"]);

        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains("aria-haspopup=\"listbox\"", html);
        Assert.Contains("popover=\"auto\"", html);
    }

    [Fact]
    public void The_browser_owns_opening_and_dismissal()
    {
        // popovertarget is what makes the browser open it, and the toggle event is the only writer of
        // the open flag — the pair is what keeps aria-expanded truthful.
        var html = Live();

        Assert.Contains("popovertarget=", html);
        Assert.Contains("data-rask-on-toggle", html);
    }

    [Fact]
    public void The_drawn_box_takes_the_keyboard()
    {
        Assert.Contains("data-rask-on-keydown", Live());
    }

    [Fact]
    public void The_native_control_reports_the_whole_selection_back()
    {
        // Not the DOM's single `value`, which for a multi-select is only the FIRST picked option — the
        // reason this control takes the raw values rather than forwarding Bind to Select.
        var html = global::Rask.Testing.RaskTest.Render(
            UiMultiSelect.Value<string>(["core"]).Options(Packages).Label("Packages")).Html;

        Assert.Contains("data-rask-on-change", html);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Native(string[] chosen) =>
        UiMultiSelect.Value<string>(chosen).Options(Packages).Label("Packages").ToHtml();

    private static string Custom(string[] chosen) =>
        UiMultiSelect.Value<string>(chosen).Options(Packages).Label("Packages").Native(false).ToHtml();

    // Handlers only exist inside a live render, so the attribute assertions above need one.
    private static string Live() =>
        global::Rask.Testing.RaskTest.Render(
            UiMultiSelect.Value<string>(["core"]).Options(Packages).Label("Packages")
                .Native(false)).Html;
}
