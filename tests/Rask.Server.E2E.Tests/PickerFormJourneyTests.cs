using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A form with pickers the kit draws, as an app's first picker page writes one — a name with a rule the
///     store answers, a searchable listbox under it, <c>ConfirmLeave</c> around both — in a real browser.
/// </summary>
/// <remarks>
///     What is chosen in a drawn control raises no <c>input</c> in the browser: the page hears the press and
///     says so on the form (<c>data-rask-edited</c>), and the guard asks. Every test counts the questions it
///     was asked, because a guard that never asked and one that was answered look the same from the page.
/// </remarks>
public sealed class PickerFormJourneyTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Message = "Elhagyja a tartályt mentés nélkül?";

    private const string Edited = "form[data-rask-edited]";

    // How the reader changes the value of each family of drawn control: what opens it, when anything does,
    // and what is then pressed.
    public static TheoryData<string, string?, string> Families => new()
    {
        { "listbox", "[data-ui-select-button]", "[data-ui-option]:visible" },
        { "several", "[data-ui-select-button]", "[data-ui-option]:visible" },
        { "combobox", "input[role=combobox]", "[data-ui-option]:visible" },
        { "pillbox", "[role=combobox]", "[data-ui-listbox-option]:visible" },
        { "autocomplete", "input", "[role=option]:visible" },
        { "calendar", null, "td[data-date]:not([data-outside]) button:not([disabled])" },
        { "date", "[data-ui-date-picker-button]", "td[data-date]:not([data-outside]) button:visible:not([disabled])" },
        { "time", "button", "button[role=option]:visible" },
        { "colour", "[data-ui-color-picker-swatch-button]", "[data-ui-color-picker-swatch]:visible" },
    };

    [Theory]
    [MemberData(nameof(Families))]
    public async Task A_value_chosen_in_a_drawn_control_is_asked_about_before_a_link_takes_the_reader_away(
        string family, string? opens, string picks)
    {
        await using var session = await HookSession.OpenAsync<PickerFormPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);
        await session.HooksLoadedAsync();

        await ChooseAsync(page, family, opens, picks);
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("#to-list");
        await page.ClickAsync("#count");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        Assert.Equal(["confirm:" + Message], asked);
    }

    [Fact]
    public async Task A_form_whose_lists_were_only_opened_lets_the_reader_go_without_asking()
    {
        await using var session = await HookSession.OpenAsync<PickerFormPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);
        await session.HooksLoadedAsync();

        await page.ClickAsync("#listbox [data-ui-select-button]");
        await Expect(page.Locator("#listbox [data-ui-option]:visible").First).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.ClickAsync("#to-list");

        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        Assert.Empty(asked);
    }

    [Fact]
    public async Task A_pick_that_was_saved_is_not_asked_about_and_one_made_after_the_save_is()
    {
        await using var session = await HookSession.OpenAsync<PickerFormPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, stay: true);
        await session.HooksLoadedAsync();

        await ChooseAsync(page, "listbox", "[data-ui-select-button]", "[data-ui-option]:visible");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=|Benzin");
        await Expect(page.Locator(Edited)).ToHaveCountAsync(0);
        await page.ClickAsync("#to-list");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        var askedAfterTheSave = asked.Count;
        await page.ClickAsync("#to-form");
        await ChooseAsync(page, "calendar", null, "td[data-date]:not([data-outside]) button:not([disabled])");
        await page.ClickAsync("#to-list");
        await page.ClickAsync("#count");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        Assert.Equal(0, askedAfterTheSave);
        Assert.Equal(["confirm:" + Message], asked);
    }

    // The page has heard the press once the form says so: that is what the guard reads, and what a reader's
    // next press — a human's — comes after.
    private static async Task ChooseAsync(IPage page, string family, string? opens, string picks)
    {
        var control = page.Locator("#" + family);
        if (opens is not null)
        {
            await control.Locator(opens).First.ClickAsync();
        }

        await control.Locator(picks).First.ClickAsync();
        await Expect(page.Locator(Edited)).ToHaveCountAsync(1);
    }

    private static List<string> Answer(IPage page, bool stay)
    {
        List<string> asked = [];
        page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ":" + dialog.Message);
            _ = stay ? dialog.DismissAsync() : dialog.AcceptAsync();
        };
        return asked;
    }
}

internal sealed class TankDraft
{
    public string Name { get; set; } = "";

    public string? Fuel { get; set; }

    public List<string> Fuels { get; set; } = [];

    public string? Kind { get; set; }

    public List<string> Tags { get; set; } = [];

    public string City { get; set; } = "";

    public DateOnly Day { get; set; }

    public DateOnly? From { get; set; }

    public TimeOnly? At { get; set; }

    public string Colour { get; set; } = "";
}

/// <summary>A guarded form of drawn pickers at <c>/</c>, each in a box named after its family, and a path to leave it for.</summary>
public sealed partial class PickerFormPage(RouteState route) : Component
{
    internal const string Taken = "Ilyen néven már létezik tartály.";

    private static readonly string[] Fuels = ["Benzin", "Dízel", "AdBlue"];

    private readonly TankDraft _tank = new();
    private string _saved = "";
    private int _count;

    protected override Component? HeadAssets => Markup.Title["picker form"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        P.Id("path")[$"path={route.Path}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("counted")[$"count={_count}"],
        Button.Id("count").OnClick(() => _count++)["count"],
        NavLink.Href("/").Id("to-form")["form"],
        NavLink.Href("/list").Id("to-list")["list"],
        string.Equals(route.Path, "/", StringComparison.Ordinal) ? TankForm() : null
    ];

    private Component TankForm() =>
        Form.Model(_tank).OnSubmit(Save).ConfirmLeave("Elhagyja a tartályt mentés nélkül?")[
            Div.Id("name-field")[
                Ui.Field[
                    Ui.Label["Megnevezés"],
                    Ui.Input.Bind(() => _tank.Name).Id("name").Validate(name => name == "Keleti" ? [Taken] : []),
                    Ui.Error
                ]
            ],
            Div.Id("listbox")[Ui.Select.Bind(() => _tank.Fuel).Listbox.Searchable().Placeholder("Válasszon…")[Options()]],
            Div.Id("several")[Ui.Select.Bind(() => _tank.Fuels).Listbox.Multiple().Placeholder("Válasszon…")[Options()]],
            Div.Id("combobox")[Ui.Select.Bind(() => _tank.Kind).Combobox.Placeholder("Válasszon…")[Options()]],
            Div.Id("pillbox")[Ui.Pillbox.Bind(() => _tank.Tags).Placeholder("Válasszon…")[Fuels.Select(fuel => Ui.PillboxOption.Key(fuel)[fuel])]],
            Div.Id("autocomplete")[Ui.Autocomplete.Bind(() => _tank.City)[Fuels.Select(fuel => Ui.AutocompleteItem.Key(fuel)[fuel])]],
            Div.Id("calendar")[Ui.Calendar.Bind(() => _tank.Day)],
            Div.Id("date")[Ui.DatePicker.Bind(() => _tank.From)],
            Div.Id("time")[Ui.TimePicker.Bind(() => _tank.At)],
            Div.Id("colour")[Ui.ColorPicker.Bind(() => _tank.Colour)],
            NavLink.Href("/list").Id("cancel")["Mégse"],
            Ui.Button.Primary.Submit.Id("save")["Mentés"]
        ];

    private static IEnumerable<Component> Options() => Fuels.Select(fuel => Ui.SelectOption.Key(fuel).Value(fuel)[fuel]);

    private void Save() => _saved = $"{_tank.Name}|{_tank.Fuel}";
}
