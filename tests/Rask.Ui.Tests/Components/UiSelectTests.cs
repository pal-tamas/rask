namespace Rask.Ui.Tests.Components;

/// <summary>
///     The select, in both of its modes.
/// </summary>
/// <remarks>
///     The chain opens on <c>Value</c> or <c>Bind</c>, which is what fixes both the type argument and
///     the mode; <c>Label</c> and <c>Options</c> follow in either order.
/// </remarks>
public partial class UiSelectTests : global::Rask.Core.RaskMarkup
{
    private static readonly (string Value, string Text)[] Countries =
    [
        ("hu", "Hungary"), ("gb", "United Kingdom"), ("ie", "Ireland")
    ];

    [Fact]
    public void The_default_is_the_platforms_own_control()
    {
        // The version to reach for: it works with a keyboard, a screen reader and a phone's picker
        // without a line of script, and it renders complete on a prerendered page.
        var html = Native(null);

        Assert.Contains("<select", html);
        Assert.DoesNotContain("role=\"combobox\"", html);
    }

    [Fact]
    public void Native_true_is_the_same_as_leaving_it_unset() =>
        Assert.Equal(Native(null), Native(true));

    [Fact]
    public void Turning_it_off_draws_a_combobox_over_a_listbox()
    {
        var html = Native(false);

        Assert.DoesNotContain("<select", html);
        Assert.Contains("role=\"combobox\"", html);
        Assert.Contains("role=\"listbox\"", html);
        Assert.Contains("role=\"option\"", html);
    }

    [Fact]
    public void The_combobox_carries_the_whole_popup_contract()
    {
        // aria-haspopup/expanded/controls is the contract docs/forms.md documents for this pattern, and
        // aria-expanded is only truthful because the list reports the browser's own dismissal back.
        var html = Native(false);

        Assert.Contains("aria-haspopup=\"listbox\"", html);
        Assert.Contains("aria-expanded=\"false\"", html);
        Assert.Contains("aria-controls=", html);
    }

    [Fact]
    public void The_list_is_a_popover_named_by_its_box()
    {
        // The browser owns dismissal: top layer, Escape and click-outside, none of it implemented here.
        var html = Native(false);

        Assert.Contains("popover=\"auto\"", html);
        Assert.Contains("popovertarget=", html);
        Assert.Contains("popovertargetaction=\"hide\"", html);
    }

    [Fact]
    public void The_list_hears_the_browser_closing_it()
    {
        // Without this, aria-expanded above goes on saying "true" over a list Escape already closed.
        Assert.Contains("data-rask-on-toggle=", Live(Native(false)));
    }

    [Fact]
    public void The_box_takes_the_keyboard()
    {
        Assert.Contains("data-rask-on-keydown=", Live(Native(false)));
    }

    [Fact]
    public void The_selected_option_is_marked_for_CSS_and_for_a_screen_reader()
    {
        var html = Custom("gb");

        Assert.Contains("menu-active", html);
        Assert.Contains("aria-selected=\"true\"", html);
    }

    [Fact]
    public void The_box_shows_the_selected_options_words_rather_than_its_value() =>
        Assert.Contains("United Kingdom", Custom("gb"));

    [Fact]
    public void With_nothing_selected_the_box_shows_the_placeholder() =>
        Assert.Contains("Choose",
            UiSelect.Value<string>(null).Options(Countries).Label("Country").Placeholder("Choose…")
                .Native(false).ToHtml());

    [Fact]
    public void A_disabled_option_says_so_and_is_unreachable()
    {
        // menu-disabled goes on the <li>, unlike menu-active and menu-focus which go on the child —
        // an asymmetry in daisyUI's own rules rather than a choice here.
        var html = UiSelect.Value<string>(null).Options(Countries).Label("Country").Native(false)
            .OptionDisabled(v => v == "gb").ToHtml();

        Assert.Contains("aria-disabled=\"true\"", html);
        Assert.Contains("menu-disabled", html);
    }

    [Fact]
    public void An_enabled_option_claims_nothing()
    {
        // A valueless aria-disabled reads as "true", so it has to be absent rather than empty.
        Assert.DoesNotContain("aria-disabled", Native(false));
    }

    [Fact]
    public void Groups_render_headers()
    {
        var html = UiSelect.Value<string>(null).Options(Countries).Label("Country").Native(false)
            .OptionGroup(v => v == "hu" ? "Europe" : "Isles").ToHtml();

        Assert.Contains("menu-title", html);
        Assert.Contains("Europe", html);
        Assert.Contains("Isles", html);
    }

    [Fact]
    public void The_hidden_input_appears_only_when_the_field_is_named()
    {
        // A listbox of buttons submits nothing, so without it a control inside a plain <form> would
        // silently drop its field.
        Assert.DoesNotContain("type=\"hidden\"", Custom("gb"));
        Assert.Contains("type=\"hidden\"",
            UiSelect.Value("gb").Options(Countries).Label("Country").Native(false).Name("country")
                .ToHtml());
    }

    [Fact]
    public void The_native_mode_ignores_Name_because_a_select_posts_itself() =>
        Assert.DoesNotContain("type=\"hidden\"",
            UiSelect.Value("gb").Options(Countries).Label("Country").Name("country").ToHtml());

    [Fact]
    public void Both_modes_name_themselves()
    {
        Assert.Contains("aria-label=\"Country\"", Native(null));
        Assert.Contains("aria-label=\"Country\"", Native(false));
    }

    [Fact]
    public void An_errored_control_says_so_in_both_modes()
    {
        Assert.Contains("aria-invalid=\"true\"",
            UiSelect.Value("gb").Options(Countries).Label("Country").Tone(UiTone.Error).ToHtml());
        Assert.Contains("aria-invalid=\"true\"",
            UiSelect.Value("gb").Options(Countries).Label("Country").Tone(UiTone.Error).Native(false)
                .ToHtml());
    }

    [Fact]
    public void The_native_mode_still_offers_an_unselectable_placeholder()
    {
        // A placeholder that can be chosen is an answer, and one chosen by accident is a bug report
        // about a form that saved nothing.
        var html = UiSelect.Value<string>(null).Options(Countries).Label("Country")
            .Placeholder("Choose…").ToHtml();

        Assert.Contains("disabled", html);
        Assert.Contains("Choose", html);
    }

    private static string Native(bool? native) =>
        UiSelect.Value("gb").Options(Countries).Label("Country").Native(native).ToHtml();

    private static string Custom(string? value) =>
        UiSelect.Value(value).Options(Countries).Label("Country").Native(false).ToHtml();

    // Handlers only exist inside a live render, so the attribute assertions above need one.
    private static string Live(string _) =>
        global::Rask.Testing.RaskTest.Render(
            UiSelect.Value("gb").Options(Countries).Label("Country").Native(false)).Html;
}
