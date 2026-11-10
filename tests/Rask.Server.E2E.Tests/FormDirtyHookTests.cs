using System.ComponentModel.DataAnnotations;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The <c>data-dirty</c> marks on a live SERVER page, in a real browser: a field and its form are marked the
///     moment the reader changes them, unmarked when the value is put back, kept by a refused save and taken by an
///     accepted one, and <c>EditContext.Reset()</c> puts every value back — with the leave guard agreeing at each step.
/// </summary>
/// <remarks>
///     At a desktop's width and a phone's: the marks are attributes, but the pillbox a phone draws differently is
///     one of the controls marked.
/// </remarks>
public sealed class FormDirtyHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    public static TheoryData<int, int> Screens => new() { { 1280, 800 }, { 390, 844 } };

    private static async Task<HookSession> OpenAsync(PlaywrightFixture playwright, int width, int height)
    {
        var session = await HookSession.OpenAsync<DirtyFormPage>(playwright, new() { ViewportSize = new() { Width = width, Height = height } });
        await session.HooksLoadedAsync();
        return session;
    }

    private static ILocator Form(IPage page) => page.Locator("#order");

    private static Task Marked(ILocator el) => Expect(el).ToHaveAttributeAsync("data-dirty", string.Empty);

    private static Task Unmarked(ILocator el) => Expect(el).Not.ToHaveAttributeAsync("data-dirty", string.Empty);

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_field_typed_into_marks_itself_and_its_form_and_typed_back_marks_neither(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;
        await Unmarked(Form(page));

        await page.FillAsync("#title", "Barrel");
        await Marked(page.Locator("#title"));
        await Marked(Form(page));
        await Unmarked(page.Locator("#city"));
        await page.FillAsync("#title", "Tank");

        await Unmarked(page.Locator("#title"));
        await Unmarked(Form(page));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_checkbox_a_select_a_date_and_a_nested_field_each_mark_by_their_own_value(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;

        await page.CheckAsync("#urgent");
        await Marked(page.Locator("#urgent"));
        await page.UncheckAsync("#urgent");
        await Unmarked(page.Locator("#urgent"));
        await page.SelectOptionAsync("#size", "large");
        await Marked(page.Locator("#size"));
        await page.SelectOptionAsync("#size", "small");
        await Unmarked(page.Locator("#size"));
        await page.FillAsync("#due", "2026-12-24");
        await Marked(page.Locator("#due"));
        await page.FillAsync("#city", "Győr");

        await Marked(page.Locator("#city"));
        await Marked(Form(page));
        await Unmarked(page.Locator("#size"));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_pick_in_a_pillbox_marks_the_form_and_an_accepted_save_takes_every_mark(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;
        await page.Locator("#tags [data-ui-pillbox-input], #tags [data-ui-pillbox-trigger]").First.ClickAsync();
        await page.Locator("#tags [role=option]").Filter(new() { HasText = "Diesel" }).ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#tags [data-ui-pillbox-trigger] [data-value]")).ToHaveCountAsync(1);
        await Marked(Form(page));
        await page.FillAsync("#title", "Barrel");

        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Barrel;Diesel");
        await Unmarked(Form(page));
        await Unmarked(page.Locator("#title"));
        await Expect(page.Locator("#unsaved")).ToHaveTextAsync("unsaved=False");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_refused_save_keeps_every_mark_and_the_server_agrees(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;
        await page.FillAsync("#title", "");

        await page.ClickAsync("#save");

        await Expect(page.Locator("#refused")).ToHaveTextAsync("refused=1");
        await Marked(page.Locator("#title"));
        await Marked(Form(page));
        await Expect(page.Locator("#unsaved")).ToHaveTextAsync("unsaved=True");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task Starting_again_puts_every_value_back_takes_every_mark_and_the_guard_lets_the_reader_go(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;
        var asked = new List<string>();
        page.Dialog += async (_, dialog) =>
        {
            asked.Add(dialog.Message);
            await dialog.DismissAsync();
        };
        await page.FillAsync("#title", "");
        await page.CheckAsync("#urgent");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#refused")).ToHaveTextAsync("refused=1");

        await page.ClickAsync("#again");

        await Expect(page.Locator("#title")).ToHaveValueAsync("Tank");
        await Expect(page.Locator("#urgent")).Not.ToBeCheckedAsync();
        await Unmarked(page.Locator("#title"));
        await Unmarked(Form(page));
        await Expect(page.Locator("#order [data-rask-validation-message], #order .validation-message")).ToHaveCountAsync(0);
        await page.ClickAsync("#to-other");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/other");
        Assert.Empty(asked);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task The_leave_guard_still_asks_while_a_value_is_marked(int width, int height)
    {
        await using var session = await OpenAsync(playwright, width, height);
        var page = session.Page;
        var asked = new List<string>();
        page.Dialog += async (_, dialog) =>
        {
            asked.Add(dialog.Message);
            await dialog.DismissAsync();
        };
        await page.FillAsync("#title", "Barrel");

        await page.ClickAsync("#to-other");

        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        Assert.Equal(["Leave the order?"], asked);
        await Marked(Form(page));
    }
}

/// <summary>A tank order: a text, a checkbox, a select, a date, a nested address and a pillbox, all bound.</summary>
public sealed partial class DirtyFormPage : Component
{
    private readonly RouteState _route;
    private readonly TankOrder _order = new();

    // The form's context is the page's, so a press outside the form can start it again.
    private readonly EditContext _form;
    private string _saved = "";
    private int _refused;

    public DirtyFormPage(RouteState route)
    {
        _route = route;
        _form = new EditContext(_order);
    }

    protected override Component? HeadAssets => [Markup.Title["dirty form"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("path")[$"path={_route.Path}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("refused")[$"refused={_refused}"],
        NavLink.Href("/other").Id("to-other")["other"],
        string.Equals(_route.Path, "/", StringComparison.Ordinal)
            ? Form.Model(_order).Context(_form).OnSubmit(Save).OnInvalidSubmit(_ => _refused++).NoValidate(true)
                .ConfirmLeave("Leave the order?").Id("order")[f => [
                    Input.Bind(() => _order.Title).Id("title"),
                    Input.Bind(() => _order.Urgent).Id("urgent"),
                    Select.Bind(() => _order.Size).Id("size")[
                        Option.Value("small")["Small"],
                        Option.Value("large")["Large"]
                    ],
                    Input.Bind(() => _order.Due).Id("due"),
                    Input.Bind(() => _order.Address.City).Id("city"),
                    Div.Id("tags")[
                        Ui.Pillbox.Bind(() => _order.Fuels)[
                            Ui.PillboxOption.Key("Diesel").Value("Diesel")["Diesel"],
                            Ui.PillboxOption.Key("Petrol").Value("Petrol")["Petrol"]
                        ]
                    ],
                    P.Id("unsaved")[$"unsaved={f.Unsaved}"],
                    Button.Type(ButtonType.Submit).Id("save")["save"],
                    Button.Type(ButtonType.Button).Id("again").OnClick(() => _form.Reset())["start again"]
                ]]
            : null
    ];

    private void Save(TankOrder order) => _saved = order.Title + ";" + string.Join(",", order.Fuels);
}

public sealed class TankOrder
{
    [Required]
    public string Title { get; set; } = "Tank";

    public bool Urgent { get; set; }

    public string Size { get; set; } = "small";

    public DateOnly Due { get; set; } = new(2026, 12, 1);

    public TankAddress Address { get; set; } = new();

    public List<string> Fuels { get; set; } = [];
}

public sealed class TankAddress
{
    public string City { get; set; } = "Pécs";
}
