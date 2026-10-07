using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>flux:pillbox</c>: several answers out of a list, each shown as a pill that can be taken off again.
/// </summary>
/// <remarks>As recorded on fluxui.dev, key by key; <c>scripts/flux/parity-pillbox.mjs --record</c> replays the walks.</remarks>
public partial class UiPillboxTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";

    private static readonly string[] Tags = ["Design", "Development", "Marketing", "Sales", "Support"];

    private readonly List<(int Id, string Name)> _known = [(1, "Design"), (2, "Development")];

    private List<string> _picked = [];
    private List<int> _ids = [];
    private string _search = "";

    private Component Pillbox() =>
        Ui.Pillbox.Values(_picked).OnChange(picked => _picked = [.. picked]).Placeholder("Choose tags...")[
            Tags.Select(name => Ui.PillboxOption.Key(name)[name])
        ];

    private Component Searchable() =>
        Ui.Pillbox.Values(_picked).OnChange(picked => _picked = [.. picked]).Searchable().SearchPlaceholder("Filter tags...")[
            Tags.Select(name => Ui.PillboxOption.Key(name)[name])
        ];

    private Component Combobox() =>
        Ui.Pillbox.Values(_picked).OnChange(picked => _picked = [.. picked]).Combobox.Placeholder("Choose tags...")[
            Tags.Select(name => Ui.PillboxOption.Key(name)[name])
        ];

    private Component Creating() =>
        Ui.Pillbox.Values(_ids).OnChange(picked => _ids = [.. picked]).Combobox[
            Ui.PillboxInput.Value(_search).OnInput(text => _search = text).Placeholder("Choose tags..."),
            _known.Select(tag => Ui.PillboxOption.Key(tag.Id).Value(tag.Id)[tag.Name]),
            Ui.PillboxOptionCreate.MinLength(2).OnClick(Create)["Create new"]
        ];

    private void Create(string name)
    {
        _known.Add((9, name));
        _ids = [.. _ids, 9];
        _search = "";
    }

    private static Task Key(Page page, string key, string on = "[data-ui-pillbox-trigger]") =>
        page.On(on).Raise("keydown", $"{{\"key\":\"{key}\"}}");

    private static bool IsOpen(Page page) =>
        string.Equals(page.Find("[popover]").Attribute("data-rask-popover-open"), "true", StringComparison.Ordinal);

    private static IEnumerable<string> Pills(Page page) =>
        page.FindAll("[data-ui-pillbox-trigger] [data-value]").Select(pill => pill.TextContent.Trim());

    private static IEnumerable<string> Shown(Page page) =>
        page.FindAll("[data-ui-listbox-option]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim());

    [Fact]
    public void The_trigger_is_a_combobox_that_shows_the_placeholder_while_nothing_is_picked()
    {
        var page = Page.Render(Pillbox);

        var trigger = page.Find("[data-ui-control][data-ui-pillbox] > [data-ui-pillbox-trigger]");

        Assert.Equal("combobox", trigger.Attribute("role"));
        Assert.Equal("0", trigger.Attribute("tabindex"));
        Assert.Equal("none", trigger.Attribute("aria-autocomplete"));
        Assert.Equal("listbox", trigger.Attribute("aria-haspopup"));
        Assert.Equal(page.Find("[data-ui-listbox-options]").Id, trigger.Attribute("aria-controls"));
        Assert.Equal("Choose tags...", page.TextOf("[data-ui-pillbox-placeholder]"));
    }

    [Fact]
    public void The_trigger_keeps_the_keys_Flux_keeps_so_the_page_behind_it_does_not_move()
    {
        var (plain, searched) = (Page.Render(Pillbox), Page.Render(Searchable));

        var combobox = plain.Find("[data-ui-pillbox-trigger]").Attribute("data-rask-contain-keys");
        var button = searched.Find("[data-ui-pillbox-trigger]").Attribute("data-rask-contain-keys");

        Assert.Equal("Enter Space ArrowUp ArrowDown", combobox);
        Assert.Equal("Space ArrowUp ArrowDown", button);
    }

    [Fact]
    public async Task The_input_among_the_pills_says_no_expanded_state_and_keeps_the_lists_keys_only_while_it_is_open()
    {
        var page = Page.Render(Combobox);
        var closed = page.Find("[data-ui-pillbox-input]").Attribute("data-rask-contain-keys");

        await Key(page, "ArrowDown", "[data-ui-pillbox-input]");
        var input = page.Find("[data-ui-pillbox-input]");

        Assert.Null(closed);
        Assert.True(IsOpen(page));
        Assert.Equal("Enter ArrowUp ArrowDown Home End PageUp PageDown", input.Attribute("data-rask-contain-keys"));
        Assert.Null(input.Attribute("aria-expanded"));
    }

    [Fact]
    public void The_list_takes_several_answers()
    {
        var page = Page.Render(Pillbox);

        var list = page.Find("[data-ui-listbox-options][popover]");

        Assert.Equal("listbox", list.Attribute("role"));
        Assert.Equal("true", list.Attribute("aria-multiselectable"));
        Assert.Equal(Tags, Shown(page));
    }

    [Fact]
    public void Every_picked_option_is_a_pill_in_the_order_it_was_picked()
    {
        _picked = ["Sales", "Design"];

        var page = Page.Render(Pillbox);

        Assert.Equal(["Sales", "Design"], Pills(page));
        Assert.Empty(page.FindAll("[data-ui-pillbox-placeholder]"));
        Assert.Equal("true", page.Find("[data-ui-listbox-option]:has-text(\"Sales\")").Attribute("aria-selected"));
    }

    [Fact]
    public async Task A_click_on_the_trigger_opens_the_list_and_another_closes_it()
    {
        var page = Page.Render(Pillbox);

        await page.On("[data-ui-pillbox-trigger]").Click();
        var opened = IsOpen(page);
        await page.On("[data-ui-pillbox-trigger]").Click();

        Assert.True(opened);
        Assert.False(IsOpen(page));
    }

    [Fact]
    public async Task Space_opens_the_closed_list_and_Enter_does_not()
    {
        var page = Page.Render(Pillbox);

        await Key(page, Keys.Enter);
        var afterEnter = IsOpen(page);
        await Key(page, " ");

        Assert.False(afterEnter);
        Assert.True(IsOpen(page));
    }

    [Fact]
    public async Task Enter_switches_the_active_row_and_leaves_the_list_open()
    {
        var page = Page.Render(Pillbox);
        await Key(page, Keys.ArrowDown);
        await page.On("[popover]").Raise("toggle", Opened);
        await Key(page, Keys.ArrowDown);

        await Key(page, Keys.Enter);
        var once = _picked.ToList();
        await Key(page, Keys.Enter);

        Assert.Equal(["Development"], once);
        Assert.Empty(_picked);
        Assert.True(IsOpen(page));
    }

    [Fact]
    public async Task A_letter_typed_on_the_closed_trigger_picks_the_option_that_starts_with_it()
    {
        var page = Page.Render(Pillbox);

        await Key(page, "s");

        Assert.Equal(["Sales"], _picked);
        Assert.False(IsOpen(page));
    }

    [Fact]
    public async Task The_cross_on_a_pill_takes_that_option_off()
    {
        _picked = ["Design", "Sales"];
        var page = Page.Render(Pillbox);

        await page.On("[data-value=\"Design\"] > .shrink-0").Click();

        Assert.Equal(["Sales"], _picked);
        Assert.False(IsOpen(page));
    }

    [Fact]
    public async Task The_pointer_leaving_the_list_leaves_no_row_lit()
    {
        var page = Page.Render(Pillbox);
        await page.On("[data-ui-pillbox-trigger]").Click();
        await page.On("[data-ui-listbox-option]:has-text(\"Sales\")").Raise("mouseenter", "{\"clientX\":10,\"clientY\":10}");
        var hovered = page.TextOf("[data-active]");

        await page.On("[role=\"listbox\"]").Raise("mouseleave");

        Assert.Equal("Sales", hovered);
        Assert.Empty(page.FindAll("[data-active]"));
    }

    [Fact]
    public async Task A_row_that_comes_under_a_resting_pointer_after_a_pick_takes_the_cursor()
    {
        var page = Page.Render(Pillbox);
        await page.On("[data-ui-pillbox-trigger]").Click();
        await page.On("[data-ui-listbox-option]:has-text(\"Sales\")").Raise("click", "{\"clientX\":40,\"clientY\":90}");

        await page.On("[data-ui-listbox-option]:has-text(\"Marketing\")").Raise("mouseenter", "{\"clientX\":40,\"clientY\":90}");

        Assert.Equal(["Sales"], _picked);
        Assert.Equal("Marketing", page.TextOf("[data-active]"));
    }

    [Fact]
    public async Task The_click_that_made_the_browser_shut_the_list_does_not_open_it_again()
    {
        var page = Page.Render(Pillbox);
        await page.On("[data-ui-pillbox-trigger]").Click();
        await page.On("[popover]").Raise("toggle", Opened);

        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"open\",\"newState\":\"closed\"}");
        await page.On("[data-ui-pillbox-trigger]").Click();

        Assert.False(IsOpen(page));
    }

    [Fact]
    public void A_searchable_pillbox_is_opened_by_a_button_over_a_search_field()
    {
        var page = Page.Render(Searchable);

        var trigger = page.Find("[data-ui-pillbox-trigger]");

        Assert.Equal("button", trigger.Attribute("role"));
        Assert.Null(trigger.Attribute("aria-controls"));
        Assert.Equal("Filter tags...", page.Find("[data-ui-options] > [data-ui-pillbox-search] > input[data-ui-pillbox-input]").Attribute("placeholder"));
        Assert.Equal("listbox", page.Find("[data-ui-options] > [role=\"listbox\"]").Attribute("role"));
    }

    [Fact]
    public async Task A_pick_empties_the_search_and_sends_the_cursor_back_to_the_top()
    {
        var page = Page.Render(Searchable);
        await page.On("[data-ui-pillbox-trigger]").Click();
        await page.On("[data-ui-pillbox-search] input").Input("s");
        await Key(page, Keys.ArrowDown, "[data-ui-pillbox-search] input");

        await Key(page, Keys.Enter, "[data-ui-pillbox-search] input");

        Assert.Single(_picked);
        Assert.Equal(Tags, Shown(page));
        Assert.Equal("Design", page.TextOf("[data-active]"));
    }

    [Fact]
    public void The_combobox_holds_an_input_among_its_pills()
    {
        _picked = ["Design"];

        var page = Page.Render(Combobox);

        var input = page.Find("[data-ui-pillbox-trigger] input[data-ui-pillbox-input]");
        Assert.Equal("button", page.Find("[data-ui-pillbox-trigger]").Attribute("role"));
        Assert.Equal("-1", page.Find("[data-ui-pillbox-trigger]").Attribute("tabindex"));
        Assert.Equal("combobox", input.Attribute("role"));
        Assert.Equal("list", input.Attribute("aria-autocomplete"));
        Assert.Equal("", input.Attribute("placeholder"));
        Assert.Equal("Choose tags...", input.Attribute("data-placeholder"));
    }

    [Fact]
    public async Task Typing_in_the_combobox_opens_and_narrows_the_list()
    {
        var page = Page.Render(Combobox);

        await page.On("[data-ui-pillbox-input]").Input("de");

        Assert.True(IsOpen(page));
        Assert.Equal(["Design", "Development"], Shown(page));
        Assert.Equal("Design", page.TextOf("[data-active]"));
    }

    [Fact]
    public async Task Backspace_in_the_empty_input_takes_the_last_pill_off()
    {
        _picked = ["Design", "Sales"];
        var page = Page.Render(Combobox);

        await Key(page, Keys.Backspace, "[data-ui-pillbox-input]");

        Assert.Equal(["Design"], _picked);
    }

    [Fact]
    public async Task The_create_row_is_offered_once_what_was_typed_is_long_enough_and_names_no_option()
    {
        var page = Page.Render(Creating);

        await page.On("[data-ui-pillbox-input]").Input("d");
        var short_ = page.Find("[data-ui-option-create]").Attribute("data-hidden");
        await page.On("[data-ui-pillbox-input]").Input("design");
        var named = page.Find("[data-ui-option-create]").Attribute("data-hidden");
        await page.On("[data-ui-pillbox-input]").Input("Research");

        Assert.NotNull(short_);
        Assert.NotNull(named);
        Assert.Null(page.Find("[data-ui-option-create]").Attribute("data-hidden"));
    }

    [Fact]
    public async Task Picking_the_create_row_hands_the_page_what_was_typed()
    {
        var page = Page.Render(Creating);
        await page.On("[data-ui-pillbox-input]").Input("Research");

        await page.On("[data-ui-option-create]").Click();

        Assert.Equal([9], _ids);
        Assert.Equal(["Research"], Pills(page));
    }

    [Fact]
    public async Task An_option_that_is_not_filterable_stays_whatever_is_typed()
    {
        var page = Page.Render(() => Ui.Pillbox.Values(_picked).OnChange(picked => _picked = [.. picked]).Combobox[
            Ui.PillboxOption["Design"],
            Ui.PillboxOption.Filterable(false)["Something else"]
        ]);

        await page.On("[data-ui-pillbox-input]").Input("zz");

        Assert.Equal(["Something else"], Shown(page));
    }

    [Fact]
    public void A_small_pillbox_and_its_trigger_slot_are_drawn_as_asked()
    {
        var small = Page.Render(() => Ui.Pillbox.Values(_picked).Sm[Ui.PillboxOption["Design"]]);

        var slotted = Page.Render(() => Ui.Pillbox.Values(_picked)[
            Ui.PillboxTrigger.Placeholder("Pick some").Invalid(),
            Ui.PillboxOption["Design"]
        ]);

        Assert.Contains("min-h-6", small.Find("[data-ui-pillbox-trigger]").Attribute("class"), StringComparison.Ordinal);
        Assert.Equal("Pick some", slotted.TextOf("[data-ui-pillbox-placeholder]"));
        Assert.NotNull(slotted.Find("[data-ui-pillbox-trigger][data-invalid]"));
    }

    [Fact]
    public void A_bound_pillbox_writes_its_field_and_reads_the_collection()
    {
        var model = new Post { Tags = ["Sales"] };

        var page = Page.Render(() => Ui.Pillbox.Bind(() => model.Tags).Label("Tags").Description("What it is about.")[
            Tags.Select(name => Ui.PillboxOption.Key(name)[name])
        ]);

        Assert.Equal(["Sales"], Pills(page));
        Assert.Equal("Tags", page.TextOf("[data-ui-field] > label"));
        Assert.NotNull(page.Find("[data-ui-field] [data-ui-pillbox]"));
    }

    private sealed class Post
    {
        public List<string> Tags { get; set; } = [];
    }
}
