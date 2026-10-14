using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>variant="combobox"</c>: a text input that filters the list under it, shows its answer when it is
///     not being typed into, and offers to create the option that is not there.
/// </summary>
/// <remarks>
///     What is typed, the rows it leaves, the lit row and whether the list is open are the browser's
///     (<c>data-rask-list</c>), held to Flux key by key in a browser (<c>UiListHookTests</c>,
///     <c>scripts/flux/parity-select.mjs</c>). Here: what a render writes for the browser to read, and what the
///     page hears — a pick, and the text where it asked for it.
/// </remarks>
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

    private static string? Text(Page page) => page.Find("input[role=\"combobox\"]").Attribute("value");

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
        // The page filters, so the browser hides no row.
        Assert.Equal("strict", page.Find("[data-ui-select]").Attribute("data-rask-list"));
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
        Assert.Equal(string.Empty, row.Attribute("data-rask-list-create"));
        Assert.Equal("Create new", row.TextContent.Trim());
    }

    [Fact]
    public void The_browser_hides_the_rows_a_search_leaves_out_and_puts_the_answer_back_when_the_list_shuts()
    {
        var page = Page.Render(Combobox);

        var kind = page.Find("[data-ui-select]").Attribute("data-rask-list");

        Assert.Equal("filter strict", kind);
    }

    // A letter typed used to be two round trips and two renders: the key, and the input.
    [Theory]
    [InlineData("data-rask-on-input")]
    [InlineData("data-rask-on-keydown")]
    [InlineData("data-rask-on-click")]
    [InlineData("data-rask-on-blur")]
    [InlineData("data-rask-on-change")]
    public void Typing_into_a_combobox_the_page_did_not_ask_to_hear_has_no_handler(string handler)
    {
        var page = Page.Render(Combobox);

        var input = page.Find("input[role=\"combobox\"]");

        Assert.Null(input.Attribute(handler));
    }

    [Fact]
    public void The_input_is_rendered_shut_saying_its_answer()
    {
        _picked = "Web development";

        var page = Page.Render(Combobox);

        Assert.Equal("Web development", Text(page));
        Assert.Equal("false", page.Find("input[role=\"combobox\"]").Attribute("aria-expanded"));
        Assert.Null(page.Find("input[role=\"combobox\"]").Attribute("aria-activedescendant"));
    }

    [Fact]
    public async Task A_press_on_a_row_picks_it_and_the_input_says_it()
    {
        var page = Page.Render(Combobox);

        await page.On("[data-ui-option]:has-text(\"Web development\")").Click();

        Assert.Equal("Web development", _picked);
        Assert.Equal("Web development", Text(page));
    }

    [Fact]
    public void An_option_with_words_of_its_own_for_the_input_says_them_to_the_browser()
    {
        var page = Page.Render(() => Ui.Select.Of<string>().Combobox[
            Ui.SelectOption.Value("gb").Label("United Kingdom").SelectedLabel("UK")
        ]);

        var row = page.Find("[data-ui-option]");

        Assert.Equal("UK", row.Attribute("data-rask-list-says"));
    }

    [Fact]
    public void The_create_row_is_rendered_hidden_and_says_how_many_letters_it_waits_for()
    {
        var page = Page.Render(Creating);

        var row = page.Find("[data-ui-option-create]");

        Assert.Equal("2", row.Attribute("data-rask-list-create"));
        Assert.NotNull(row.Attribute("data-hidden"));
        Assert.NotNull(page.Find("[data-ui-listbox-empty]").Attribute("data-hidden"));
    }

    [Fact]
    public async Task What_was_typed_travels_with_the_press_on_the_create_row_and_with_nothing_before_it()
    {
        var page = Page.Render(Creating);
        var input = page.Find("input[role=\"combobox\"]");

        // The browser sends a field that waits ahead of the next action: here, the press on the row.
        await page.On("input[role=\"combobox\"]").Change("new");
        await page.On("[data-ui-option-create]").Click();

        Assert.Equal("action", input.Attribute("data-rask-bind-on"));
        Assert.Null(input.Attribute("data-rask-on-input"));
        Assert.Equal("new", _created);
        Assert.Equal(9, _project);
        Assert.Equal("new", Text(page));
    }
}
