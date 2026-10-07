using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>variant="combobox"</c>: a text input that filters the list under it, shows its answer when it is
///     not being typed into, and offers to create the option that is not there.
/// </summary>
/// <remarks>As recorded on fluxui.dev, key by key; see the walk in <c>docs/ui-kit.md</c>.</remarks>
public partial class UiSelectComboboxTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Industries =
        ["Photography", "Design services", "Web development", "Accounting", "Legal services", "Consulting", "Other"];

    private readonly List<(int Id, string Name)> _projects = [(1, "Branding"), (2, "Analytics"), (3, "Security")];

    private string? _picked;
    private int? _project;
    private string _created = "";

    private Component Combobox() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Combobox.Placeholder("Choose industry...")[
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ];

    private Component Creating() =>
        Ui.Select.Value(_project).OnChange(value => _project = value).Combobox[
            Ui.SelectInput.Placeholder("Start typing..."),
            _projects.Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
            Ui.SelectOptionCreate.MinLength(2).OnClick(Create)["Create new"]
        ];

    private void Create(string name)
    {
        _created = name;
        _projects.Add((9, name));
        _project = 9;
    }

    private static Task Key(Page page, string key) =>
        page.On("input[role=\"combobox\"]").Raise("keydown", $"{{\"key\":\"{key}\"}}");

    private static string? Text(Page page) => page.Find("input[role=\"combobox\"]").Attribute("value");

    private static bool IsOpen(Page page) =>
        string.Equals(page.Find("[popover]").Attribute("data-rask-popover-open"), "true", StringComparison.Ordinal);

    private static IEnumerable<string> Shown(Page page) =>
        page.FindAll("[data-ui-option]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim());

    [Fact]
    public void The_trigger_is_a_text_input_in_the_inputs_own_box()
    {
        var page = Page.Render(Combobox);

        var input = page.Find("[data-ui-select] > [data-ui-input] > input");

        Assert.Equal("combobox", input.Attribute("role"));
        Assert.Equal("list", input.Attribute("aria-autocomplete"));
        Assert.Equal("off", input.Attribute("autocomplete"));
        Assert.Equal("Choose industry...", input.Attribute("placeholder"));
        Assert.Equal(page.Find("[role=\"listbox\"]").Id, input.Attribute("aria-controls"));
        Assert.Equal("-1", page.Find("[data-ui-input] button").Attribute("tabindex"));
    }

    [Fact]
    public async Task A_click_in_the_input_opens_the_list_on_its_first_option()
    {
        var page = Page.Render(Combobox);

        await page.On("input[role=\"combobox\"]").Click();

        Assert.True(IsOpen(page));
        Assert.Equal("Photography", page.TextOf("[data-active]"));
        Assert.Equal(page.Find("[data-active]").Id, page.Find("input[role=\"combobox\"]").Attribute("aria-activedescendant"));
    }

    [Fact]
    public async Task Typing_opens_the_list_and_leaves_what_matches()
    {
        var page = Page.Render(Combobox);

        await page.On("input[role=\"combobox\"]").Input("de");

        Assert.True(IsOpen(page));
        Assert.Equal(["Design services", "Web development"], Shown(page));
        Assert.Equal("Design services", page.TextOf("[data-active]"));
    }

    [Fact]
    public async Task Enter_picks_the_active_match_and_the_input_shows_it()
    {
        var page = Page.Render(Combobox);
        await page.On("input[role=\"combobox\"]").Input("de");
        await Key(page, "ArrowDown");

        await Key(page, "Enter");

        Assert.Equal("Web development", _picked);
        Assert.Equal("Web development", Text(page));
        Assert.False(IsOpen(page));
    }

    [Fact]
    public async Task An_arrow_reopens_on_the_answer_with_every_option_back()
    {
        _picked = "Web development";
        var page = Page.Render(Combobox);

        await Key(page, "ArrowDown");

        Assert.True(IsOpen(page));
        Assert.Equal(7, Shown(page).Count());
        Assert.Equal("Web development", page.TextOf("[data-active]"));
    }

    [Fact]
    public async Task Escape_closes_the_list_and_then_empties_the_words_but_not_the_answer()
    {
        _picked = "Web development";
        var page = Page.Render(Combobox);
        await Key(page, "ArrowDown");

        await Key(page, "Escape");
        var closed = (IsOpen(page), Text(page));
        await Key(page, "Escape");

        Assert.Equal((false, "Web development"), closed);
        Assert.Equal(string.Empty, Text(page) ?? string.Empty);
        Assert.Equal("Web development", _picked);
    }

    [Fact]
    public async Task Leaving_with_words_that_name_nothing_puts_the_answer_back()
    {
        _picked = "Design services";
        var page = Page.Render(Combobox);
        await page.On("input[role=\"combobox\"]").Input("Design servicesxyz");
        var typed = page.Find("[data-ui-listbox-empty]").Attribute("data-hidden");

        await Key(page, "Tab");

        Assert.Null(typed);
        Assert.False(IsOpen(page));
        Assert.Equal("Design services", Text(page));
        Assert.Equal("Design services", _picked);
    }

    [Fact]
    public async Task The_create_row_waits_for_enough_letters_that_name_no_option()
    {
        var page = Page.Render(Creating);
        var hidden = new List<bool>();

        foreach (var typed in new[] { "n", "ne", "security" })
        {
            await page.On("input[role=\"combobox\"]").Input(typed);
            hidden.Add(page.Find("[data-ui-option-create]").Attribute("data-hidden") is not null);
        }

        Assert.Equal([true, false, true], hidden);
    }

    [Fact]
    public async Task The_create_row_is_the_active_one_when_nothing_else_matches_and_hands_over_what_was_typed()
    {
        var page = Page.Render(Creating);
        await page.On("input[role=\"combobox\"]").Input("new");
        var active = page.Find("[data-active]").Attribute("data-ui-option-create");
        var empty = page.Find("[data-ui-listbox-empty]").Attribute("data-hidden");

        await Key(page, "Enter");

        Assert.NotNull(active);
        Assert.NotNull(empty);
        Assert.Equal("new", _created);
        Assert.Equal("new", Text(page));
        Assert.False(IsOpen(page));
    }

    [Fact]
    public async Task With_filter_off_the_page_answers_the_search_and_a_spinner_waits_beside_the_chevron()
    {
        var heard = "";
        var page = Page.Render(() => Ui.Select.Of<int?>().Combobox.Filter(false)[
            Ui.SelectInput.Placeholder("Start typing...").OnInput(text => heard = text),
            _projects.Where(project => project.Name.Contains(heard, StringComparison.OrdinalIgnoreCase))
                .Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
            Ui.SelectOptionEmpty.WhenLoading("Loading projects...")["No projects found."]
        ]);

        await page.On("input[role=\"combobox\"]").Input("sec");

        Assert.Equal("sec", heard);
        Assert.Equal(["Security"], page.FindAll("[data-ui-option]").Select(row => row.TextContent.Trim()));
        Assert.Equal(2, page.FindAll("[data-ui-input] svg").Count);
    }

    [Fact]
    public void A_listbox_with_no_search_always_offers_its_create_row()
    {
        var select = Ui.Select.Of<int?>().Listbox[
            _projects.Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
            Ui.SelectOptionCreate["Create new"]
        ];

        var row = Page.Render(select).Find("[data-ui-option-create]");

        Assert.Null(row.Attribute("data-hidden"));
        Assert.Equal("Create new", row.TextContent.Trim());
    }
}
