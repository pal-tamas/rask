using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>variant="listbox"</c>: the button, the list it opens, and what the page hears of it — a pick.
/// </summary>
/// <remarks>
///     Where the reader is in the list is the browser's: the lit row, the keys, type-ahead and whether the list
///     is open are kept by the runtime (<c>data-rask-list</c>) and send the page nothing, so they are held to
///     Flux in a browser (<c>UiListHookTests</c>, <c>scripts/flux/parity-select.mjs</c>). What is in process is
///     what a render writes, and the press that picks.
/// </remarks>
public partial class UiSelectListboxTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Industries =
        ["Photography", "Design services", "Web development", "Accounting", "Legal services", "Consulting", "Other"];

    private string? _picked;

    private Component Listbox() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Placeholder("Choose industry...")[
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ];

    [Fact]
    public void The_button_is_a_combobox_that_names_the_list_it_opens()
    {
        var page = Page.Render(Listbox);

        var button = page.Find("[data-ui-select-button]");

        Assert.Equal("button", button.Tag);
        Assert.Equal("combobox", button.Attribute("role"));
        Assert.Equal("listbox", button.Attribute("aria-haspopup"));
        Assert.Equal("false", button.Attribute("aria-expanded"));
        Assert.Equal(page.Find("[role=\"listbox\"]").Attribute("id"), button.Attribute("aria-controls"));
        Assert.Equal(page.Find("[popover]").Attribute("id"), button.Attribute("popovertarget"));
    }

    [Fact]
    public void The_list_is_a_native_popover_of_options_anchored_to_the_button()
    {
        var page = Page.Render(Listbox);

        var list = page.Find("[data-ui-options]");

        Assert.Equal("auto", list.Attribute("popover"));
        Assert.Equal("listbox", list.Attribute("role"));
        Assert.Contains("position-anchor:--uisel-", list.Attribute("style"), StringComparison.Ordinal);
        Assert.Contains("anchor-name:--uisel-", page.Find("[data-ui-select-button]").Attribute("style"), StringComparison.Ordinal);
        Assert.Equal(7, page.FindAll("[role=\"option\"][data-ui-option]").Count);
    }

    [Fact]
    public void A_list_says_multiselectable_whatever_it_holds_as_on_Flux()
    {
        var page = Page.Render(Listbox);

        var list = page.Find("[data-ui-options]");

        Assert.Equal("true", list.Attribute("aria-multiselectable"));
    }

    [Fact]
    public void The_closed_button_and_the_open_list_carry_the_runtime_hooks_for_Flux_keys_and_page_lock()
    {
        var page = Page.Render(Listbox);

        var (button, list) = (page.Find("[data-ui-select-button]"), page.Find("[data-ui-options]"));

        Assert.NotNull(button.Attribute("data-rask-listbox-button"));
        Assert.NotNull(list.Attribute("data-rask-lock"));
    }

    [Fact]
    public void Nothing_picked_shows_the_placeholder()
    {
        var page = Page.Render(Listbox);

        var placeholder = page.TextOf("[data-ui-select-placeholder]");

        Assert.Equal("Choose industry...", placeholder);
    }

    [Fact]
    public void A_prefix_stays_in_the_button_before_the_picked_option()
    {
        var select = Ui.Select.Value("last-month").Listbox.Prefix("Compare to")[
            Ui.SelectOption.Value("last-month")["Last month"]
        ];

        var page = Page.Render(select);

        Assert.Equal("Compare to", page.TextOf("[data-ui-select-prefix]"));
        Assert.Equal("Last month", page.TextOf("[data-ui-select-button] [data-value]"));
    }

    [Fact]
    public void An_option_draws_its_icon_its_avatar_and_its_description()
    {
        var select = Ui.Select.Of<string>().Listbox[
            Ui.SelectOption.Value("card").Label("Credit card").Icon(Ui.IconName.CreditCard).Description("Charged monthly"),
            Ui.SelectOption.Value("caleb").Label("Caleb Porzio").Avatar("https://example.com/caleb.png").Icon(Ui.IconName.User)
        ];

        var page = Page.Render(select);

        Assert.Equal("Charged monthly", page.TextOf("[data-ui-option] [data-ui-text]"));
        Assert.Equal("https://example.com/caleb.png", page.Find("[data-ui-avatar] img").Attribute("src"));
        // The avatar wins over the icon: the second option has the tick and nothing else drawn as an icon.
        Assert.Equal(3, page.FindAll("[data-ui-option] svg").Count);
    }

    [Fact]
    public void The_button_repeats_the_selected_label_when_an_option_has_one()
    {
        var select = Ui.Select.Value("gb").Listbox[Ui.SelectOption.Value("gb").Label("United Kingdom").SelectedLabel("UK")];

        var page = Page.Render(select);

        Assert.Equal("UK", page.TextOf("[data-ui-select-button] [data-value]"));
        Assert.Equal("United Kingdom", page.TextOf("[data-ui-option]"));
    }

    [Fact]
    public async Task Clearable_puts_a_button_beside_an_answer_that_takes_it_away()
    {
        _picked = "Consulting";
        var page = Page.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Clearable()[
                Industries.Select(name => Ui.SelectOption.Key(name)[name])
            ]);

        var inButton = UiDatePickerTests.BeforeChevron(page.Html, "data-ui-select-button");
        await page.On("[aria-label=\"Clear selected\"]").Click();

        Assert.True(inButton, "the × is not in the button, before its chevron.");
        Assert.Null(_picked);
        Assert.StartsWith("<div class=\"relative hidden items-center", UiDatePickerTests.Cross(page.Html), StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_listbox_posts_its_answer_in_a_hidden_input()
    {
        var select = Ui.Select.Value("gb").Listbox.Name("country")[Ui.SelectOption.Value("gb")["United Kingdom"]];

        var html = select.ToHtml();

        Assert.Contains("type=\"hidden\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"country\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"gb\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_heads_its_options_in_the_list()
    {
        var select = Ui.Select.Of<string>().Listbox[
            Ui.SelectGroup.Label("Creative")[Ui.SelectOption["Photography"]],
            Ui.SelectGroup.Label("Technology")[Ui.SelectOption["Web development"]]
        ];

        var page = Page.Render(select);

        Assert.Equal(2, page.FindAll("[data-ui-options] [role=\"presentation\"]").Count);
        Assert.Equal(2, page.FindAll("[data-ui-option]").Count);
        // How many rows each heading is over: the browser hides it with the last of them a search leaves out.
        Assert.Equal(["1", "1"], page.FindAll("[role=\"presentation\"]").Select(heading => heading.Attribute("data-rask-list-group")));
    }

    [Fact]
    public void The_button_slot_sets_what_the_selects_own_props_do_not()
    {
        var select = Ui.Select.Of<string>().Listbox[
            Ui.SelectButton.Placeholder("Choose industry...").Class("rounded-full!").Invalid(),
            Ui.SelectOption["Photography"]
        ];

        var page = Page.Render(select);

        Assert.Contains("rounded-full!", page.Find("[data-ui-select-button]").Attribute("class"), StringComparison.Ordinal);
        Assert.NotNull(page.Find("[data-ui-select-button]").Attribute("data-invalid"));
        Assert.Equal("Choose industry...", page.TextOf("[data-ui-select-placeholder]"));
    }

    [Fact]
    public void The_list_opens_where_position_and_align_say()
    {
        var select = Ui.Select.Of<string>().Listbox.Position(Ui.Position.Top).Align(Ui.Align.End)[Ui.SelectOption["One"]];

        var style = Page.Render(select).Find("[popover]").Attribute("style");

        Assert.Contains("bottom:calc(anchor(top) + 5px)", style, StringComparison.Ordinal);
        Assert.Contains("right:anchor(right)", style, StringComparison.Ordinal);
        Assert.Contains("position-try-fallbacks:flip-block", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_class_reaches_the_list()
    {
        var select = Ui.Select.Of<string>().Listbox.OptionsClass("min-w-72")[Ui.SelectOption["One"]];

        var classes = Page.Render(select).Find("[popover]").Attribute("class");

        Assert.Contains("min-w-72", classes, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_is_the_browsers_to_keep_and_is_rendered_shut_with_no_row_lit()
    {
        var page = Page.Render(Listbox);

        var button = page.Find("[data-ui-select-button]");

        Assert.Equal(string.Empty, page.Find("[data-ui-select]").Attribute("data-rask-list"));
        Assert.Equal("false", button.Attribute("aria-expanded"));
        Assert.Null(button.Attribute("aria-activedescendant"));
        Assert.False(page.Exists("[data-active]"));
        Assert.False(page.Exists("[data-open]"));
        Assert.Null(page.Find("[popover]").Attribute("data-rask-popover-open"));
    }

    // A round trip per row the pointer crossed, per arrow and per toggle is what these handlers were.
    [Theory]
    [InlineData("data-rask-on-mouseenter")]
    [InlineData("data-rask-on-mouseleave")]
    [InlineData("data-rask-on-keydown")]
    [InlineData("data-rask-on-toggle")]
    public void Moving_about_the_list_has_no_handler_for_the_page_to_hear(string handler)
    {
        var page = Page.Render(Listbox);

        var hearing = page.FindAll($"[{handler}]");

        Assert.Empty(hearing);
    }

    [Fact]
    public async Task A_press_on_an_option_picks_that_option_and_the_button_repeats_it()
    {
        var page = Page.Render(Listbox);

        await page.On("[data-ui-option]:has-text(\"Legal services\")").Click();

        Assert.Equal("Legal services", _picked);
        Assert.Equal("Legal services", page.TextOf("[data-ui-select-button] [data-value]"));
        Assert.Equal("true", page.Find("[data-selected][data-ui-option]").Attribute("aria-selected"));
        Assert.False(page.Exists("[data-ui-select-placeholder]"));
    }

    [Fact]
    public async Task A_press_names_its_own_row_whichever_row_was_pressed_before_it()
    {
        var page = Page.Render(Listbox);
        await page.On("[data-ui-option]:has-text(\"Other\")").Click();

        await page.On("[data-ui-option]:has-text(\"Photography\")").Click();

        Assert.Equal("Photography", _picked);
    }

    [Fact]
    public void A_disabled_option_says_so_and_has_no_press_to_hear()
    {
        var page = Page.Render(() => Ui.Select.Of<string>().Listbox[
            Ui.SelectOption["One"],
            Ui.SelectOption.Disabled()["Two"],
            Ui.SelectOption["Three"]
        ]);

        var two = page.Find("[data-ui-option]:has-text(\"Two\")");

        Assert.Equal("true", two.Attribute("aria-disabled"));
        Assert.Null(two.Attribute("data-rask-on-click"));
        Assert.NotNull(page.Find("[data-ui-option]:has-text(\"Three\")").Attribute("data-rask-on-click"));
    }

    [Fact]
    public void A_row_that_draws_more_than_its_words_says_the_words_it_is_found_by()
    {
        var page = Page.Render(() => Ui.Select.Of<string>().Listbox[
            Ui.SelectOption.Value("card").Label("Credit card").Description("Charged monthly"),
            Ui.SelectOption["Cash"]
        ]);

        var rows = page.FindAll("[data-ui-option]");

        Assert.Equal("Credit card", rows[0].Attribute("data-rask-list-text"));
        Assert.Null(rows[1].Attribute("data-rask-list-text"));
    }
}
