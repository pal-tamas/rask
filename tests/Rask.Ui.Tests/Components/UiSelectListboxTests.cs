using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>variant="listbox"</c>: the button, the list it opens, and the keyboard and pointer as they were
///     recorded on fluxui.dev — one row at a time, by the keys that walk it there.
/// </summary>
/// <remarks>
///     The browser owns whether the popover is open (a click on the button, Escape, a click elsewhere) and says
///     so through the <c>toggle</c> event, which is how these open it. Everything after that is C#, which is
///     why it can be driven in process.
/// </remarks>
public partial class UiSelectListboxTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";

    private static readonly string[] Industries =
        ["Photography", "Design services", "Web development", "Accounting", "Legal services", "Consulting", "Other"];

    private string? _picked;

    private Component Listbox() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Placeholder("Choose industry...")[
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ];

    private async Task<Page> OpenAsync()
    {
        var page = Page.Render(Listbox);
        await page.On("[popover]").Raise("toggle", Opened);

        return page;
    }

    private static Task Key(Page page, string key) =>
        page.On("[data-ui-select-button]").Raise("keydown", $"{{\"key\":\"{key}\"}}");

    private static string Active(Page page) => page.TextOf("[data-active]");

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
    public async Task Opening_puts_the_cursor_on_the_first_option_and_says_so_to_assistive_tech()
    {
        var page = await OpenAsync();

        var button = page.Find("[data-ui-select-button]");

        Assert.Equal("Photography", Active(page));
        Assert.Equal("true", button.Attribute("aria-expanded"));
        Assert.Equal(page.Find("[data-active]").Attribute("id"), button.Attribute("aria-activedescendant"));
    }

    [Fact]
    public async Task The_arrows_walk_the_options_and_stop_at_either_end()
    {
        var page = await OpenAsync();
        var walked = new List<string>();

        foreach (var key in new[] { "ArrowUp", "ArrowDown", "ArrowDown", "ArrowUp" })
        {
            await Key(page, key);
            walked.Add(Active(page));
        }

        for (var i = 0; i < 9; i++)
        {
            await Key(page, "ArrowDown");
        }

        Assert.Equal(["Photography", "Design services", "Web development", "Design services"], walked);
        Assert.Equal("Other", Active(page));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("End")]
    [InlineData("PageDown")]
    [InlineData("PageUp")]
    public async Task The_keys_Flux_leaves_alone_move_nothing(string key)
    {
        var page = await OpenAsync();
        await Key(page, "ArrowDown");

        await Key(page, key);

        Assert.Equal("Design services", Active(page));
    }

    [Fact]
    public async Task Enter_picks_the_active_option_and_closes_the_list()
    {
        var page = await OpenAsync();
        await Key(page, "ArrowDown");

        await Key(page, "Enter");

        Assert.Equal("Design services", _picked);
        Assert.Equal("false", page.Find("[data-ui-select-button]").Attribute("aria-expanded"));
        Assert.Equal("false", page.Find("[popover]").Attribute("data-rask-popover-open"));
        Assert.Equal("true", page.Find("[data-selected][data-ui-option]").Attribute("aria-selected"));
    }

    [Fact]
    public async Task A_click_on_an_option_picks_it_and_the_button_repeats_it()
    {
        var page = await OpenAsync();

        await page.On("[data-ui-option]:has-text(\"Legal services\")").Click();

        Assert.Equal("Legal services", _picked);
        Assert.Equal("Legal services", page.TextOf("[data-ui-select-button] [data-value]"));
        Assert.False(page.Exists("[data-ui-select-placeholder]"));
    }

    [Fact]
    public async Task The_pointer_moves_the_cursor_to_the_row_it_is_over()
    {
        var page = await OpenAsync();

        await page.On("[data-ui-option]:has-text(\"Accounting\")").Raise("mouseenter");
        await Key(page, "ArrowDown");

        Assert.Equal("Legal services", Active(page));
    }

    [Fact]
    public async Task Reopening_starts_from_the_picked_option()
    {
        _picked = "Consulting";

        var page = await OpenAsync();

        Assert.Equal("Consulting", Active(page));
    }

    [Fact]
    public async Task Tab_closes_the_list_and_picks_nothing()
    {
        var page = await OpenAsync();

        await Key(page, "Tab");

        Assert.Null(_picked);
        Assert.Equal("false", page.Find("[popover]").Attribute("data-rask-popover-open"));
    }

    [Theory]
    [InlineData("ArrowDown")]
    [InlineData("ArrowUp")]
    public async Task An_arrow_on_the_closed_button_opens_the_list(string key)
    {
        var page = Page.Render(Listbox);

        await Key(page, key);

        Assert.Equal("true", page.Find("[popover]").Attribute("data-rask-popover-open"));
        Assert.Equal("Photography", Active(page));
    }

    [Fact]
    public async Task Letters_typed_on_the_closed_button_pick_without_opening()
    {
        var page = Page.Render(Listbox);

        await Key(page, "l");
        await Key(page, "e");

        Assert.Equal("Legal services", _picked);
        Assert.Equal("false", page.Find("[data-ui-select-button]").Attribute("aria-expanded"));
    }

    [Fact]
    public async Task Letters_typed_on_the_open_list_move_the_cursor_and_pick_nothing()
    {
        var page = await OpenAsync();

        await Key(page, "a");
        await Key(page, "c");

        Assert.Equal("Accounting", Active(page));
        Assert.Null(_picked);
    }

    [Fact]
    public async Task The_browser_closing_the_list_is_heard()
    {
        var page = await OpenAsync();

        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"open\",\"newState\":\"closed\"}");

        Assert.Equal("false", page.Find("[data-ui-select-button]").Attribute("aria-expanded"));
        Assert.Null(page.Find("[data-ui-select-button]").Attribute("aria-activedescendant"));
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
    public async Task A_disabled_option_is_skipped_by_the_keyboard_and_deaf_to_the_pointer()
    {
        string? picked = null;
        var page = Page.Render(() => Ui.Select.Value(picked).OnChange(value => picked = value).Listbox[
            Ui.SelectOption["One"],
            Ui.SelectOption.Disabled()["Two"],
            Ui.SelectOption["Three"]
        ]);
        await page.On("[popover]").Raise("toggle", Opened);

        await Key(page, "ArrowDown");

        Assert.Equal("Three", Active(page));
        Assert.Equal("true", page.Find("[data-ui-option]:has-text(\"Two\")").Attribute("aria-disabled"));
        Assert.Null(page.Find("[data-ui-option]:has-text(\"Two\")").Attribute("data-rask-on-click"));
    }

    [Fact]
    public async Task Clearable_puts_a_button_beside_an_answer_that_takes_it_away()
    {
        _picked = "Consulting";
        var page = Page.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Clearable()[
                Industries.Select(name => Ui.SelectOption.Key(name)[name])
            ]);

        await page.On("[aria-label=\"Clear selected\"]").Click();

        Assert.Null(_picked);
        Assert.False(page.Exists("[aria-label=\"Clear selected\"]"));
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
}
