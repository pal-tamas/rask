using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>flux:pillbox</c>: several answers out of a list, each shown as a pill that can be taken off again.
/// </summary>
/// <remarks>
///     The keys, the pointer, the search and whether the list is open are the browser's (<c>data-rask-list</c>),
///     held to fluxui.dev key by key in a browser: <c>scripts/flux/parity-pillbox.mjs</c> replays the walks and
///     <c>UiListHookTests</c> counts what they send. Here: what a render writes for the browser to read, and what
///     the page hears — a row or a cross pressed.
/// </remarks>
public partial class UiPillboxTests : global::Rask.Core.RaskMarkup
{
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

    // Measured on fluxui.dev/components/pillbox (2026-10-29): zinc-300 at rest, zinc-800 under the pointer (white /
    // white at 60% in dark mode), back at rest while the list is open on a desktop, dark after a tap on a phone.
    [Fact]
    public void The_chevron_darkens_under_the_pointer_and_rests_while_the_list_is_open_where_there_is_one()
    {
        var page = Page.Render(Pillbox);

        var chevron = page.FindAll("[data-ui-pillbox-trigger] svg").Last().Attribute("class")!.Split(' ');

        Assert.Contains("text-zinc-300", chevron);
        Assert.Contains("[[data-ui-pillbox-trigger]:hover_&]:text-zinc-800", chevron);
        Assert.Contains("dark:[[data-ui-pillbox-trigger]:hover_&]:text-white", chevron);
        Assert.Contains("[@media(hover:hover)]:[[data-ui-pillbox]:has(:popover-open)_&]:text-zinc-300!", chevron);
        Assert.Contains("dark:[@media(hover:hover)]:[[data-ui-pillbox]:has(:popover-open)_&]:text-white/60!", chevron);
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
    public async Task The_cross_on_a_pill_takes_that_option_off()
    {
        _picked = ["Design", "Sales"];
        var page = Page.Render(Pillbox);

        await page.On("[data-value=\"Design\"] > [data-rask-list-remove]").Click();

        Assert.Equal(["Sales"], _picked);
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
    public async Task Picking_the_create_row_hands_the_page_what_was_typed()
    {
        var page = Page.Render(Creating);
        await page.On("[data-ui-pillbox-input]").Input("Research");

        await page.On("[data-ui-option-create]").Click();

        Assert.Equal([9], _ids);
        Assert.Equal(["Research"], Pills(page));
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

    // Measured on Flux's pillbox page at 390 × 844: a pill is 16px there, the trigger's size, and 14px from sm up.
    [Fact]
    public void A_pill_is_set_in_the_triggers_size_on_a_phone_and_a_small_one_always_in_14px()
    {
        _picked = ["Design"];

        var (plain, small) = (Page.Render(Pillbox), Page.Render(() => Ui.Pillbox.Values(_picked).Sm[Ui.PillboxOption.Key("Design")["Design"]]));

        Assert.Contains("text-base sm:text-sm", plain.Find("[data-ui-pillbox-trigger] [data-value]").Attribute("class"), StringComparison.Ordinal);
        Assert.Contains(" text-sm", small.Find("[data-ui-pillbox-trigger] [data-value]").Attribute("class"), StringComparison.Ordinal);
        Assert.DoesNotContain("text-base", small.Find("[data-ui-pillbox-trigger] [data-value]").Attribute("class"), StringComparison.Ordinal);
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

    [Theory]
    [InlineData(false, false, "multiple top")]
    [InlineData(true, false, "multiple filter top")]
    [InlineData(false, true, "multiple filter top clears")]
    public void The_list_is_the_browsers_to_keep_and_says_which_kind_it_is(bool searchable, bool combobox, string kind)
    {
        var page = Page.Render(searchable ? Searchable : combobox ? Combobox : Pillbox);

        var root = page.Find("[data-ui-pillbox]");

        Assert.Equal(kind, root.Attribute("data-rask-list"));
        Assert.Equal("false", page.Find("[data-ui-pillbox-trigger]").Attribute("aria-expanded"));
        Assert.False(page.Exists("[data-active]"));
        Assert.Null(page.Find("[popover]").Attribute("data-rask-popover-open"));
    }

    // The trigger's press and keys, the input's keys and every row the pointer crossed were a round trip each.
    [Theory]
    [InlineData("[data-ui-pillbox-trigger]", "data-rask-on-click")]
    [InlineData("[data-ui-pillbox-trigger]", "data-rask-on-keydown")]
    [InlineData("[data-ui-pillbox-input]", "data-rask-on-keydown")]
    [InlineData("[data-ui-pillbox-input]", "data-rask-on-input")]
    [InlineData("[role=\"listbox\"]", "data-rask-on-mouseleave")]
    [InlineData("[popover]", "data-rask-on-toggle")]
    [InlineData("[data-ui-listbox-option]", "data-rask-on-mouseenter")]
    public void Moving_about_a_pillbox_has_no_handler_for_the_page_to_hear(string part, string handler)
    {
        var page = Page.Render(Combobox);

        var hearing = page.FindAll(part).Select(element => element.Attribute(handler));

        Assert.All(hearing, Assert.Null);
    }

    [Fact]
    public async Task A_press_on_a_row_switches_it_on_and_another_switches_it_off()
    {
        var page = Page.Render(Pillbox);

        await page.On("[data-ui-listbox-option]:has-text(\"Development\")").Click();
        var once = _picked.ToList();
        await page.On("[data-ui-listbox-option]:has-text(\"Development\")").Click();

        Assert.Equal(["Development"], once);
        Assert.Empty(_picked);
    }

    [Fact]
    public void Every_cross_is_one_Backspace_in_the_empty_input_can_press()
    {
        _picked = ["Design", "Sales"];

        var page = Page.Render(Combobox);

        Assert.Equal(2, page.FindAll("[data-ui-pillbox-trigger] [data-value] > [data-rask-list-remove][data-rask-on-click]").Count);
    }

    [Fact]
    public void The_create_row_of_a_pillbox_typed_into_is_rendered_hidden_and_says_how_many_letters_it_waits_for()
    {
        var page = Page.Render(Creating);

        var row = page.Find("[data-ui-option-create]");

        Assert.Equal("2", row.Attribute("data-rask-list-create"));
        Assert.NotNull(row.Attribute("data-hidden"));
    }

    [Fact]
    public void An_option_that_is_not_filterable_says_no_search_hides_it()
    {
        var page = Page.Render(() => Ui.Pillbox.Values(_picked).OnChange(picked => _picked = [.. picked]).Combobox[
            Ui.PillboxOption["Design"],
            Ui.PillboxOption.Filterable(false)["Something else"]
        ]);

        var rows = page.FindAll("[data-ui-listbox-option]");

        Assert.Null(rows[0].Attribute("data-rask-list-keep"));
        Assert.NotNull(rows[1].Attribute("data-rask-list-keep"));
    }
}
