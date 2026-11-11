using System.ComponentModel.DataAnnotations;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using Rask.Wire;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A submit the page refused takes the reader to what is wrong, on a live server page: focus moves to the first
///     invalid field and it is scrolled into view — for the form's rules, for a save that refused with a field
///     failure, inside a modal, on the tab <c>Ui.FormTabs</c> opens, and for the kit's own controls. A reader who
///     moved on while the submit was in flight is left where they went.
/// </summary>
/// <remarks>
///     The runtime half is <c>rask-first-invalid.ts</c> (FirstInvalidTests on Jint); the WASM host's is the site's
///     form-tabs journey.
/// </remarks>
public sealed class FirstInvalidFieldTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // The id of what has focus, whether it is on screen, and whether it is (or is inside) a field marked invalid.
    private const string FocusState =
        """
        () => {
            const a = document.activeElement;
            const r = a.getBoundingClientRect();
            return [
                a.id || a.tagName,
                r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth,
                !!a.closest('[aria-invalid="true"]') || !!a.querySelector('[aria-invalid="true"]'),
                a.closest('form') ? a.closest('form').id : ''
            ].join('|');
        }
        """;

    private sealed record Focus(string Id, bool OnScreen, bool Invalid, string Form)
    {
        public static Focus Read(string said)
        {
            var parts = said.Split('|');
            return new Focus(parts[0], parts[1] == "true", parts[2] == "true", parts[3]);
        }
    }

    private async Task<HookSession> OpenAsync(int width, int height) =>
        await HookSession.OpenAsync<FirstInvalidPage>(playwright, new() { ViewportSize = new() { Width = width, Height = height } });

    // Read once the scroll that brings it into view has settled — smooth, for a reader who did not ask for less
    // motion — or after five seconds, when what is read says it is not on screen.
    private static async Task<Focus> FocusedAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "() => { const r = document.activeElement.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth; }",
                null,
                new() { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
            // Read below, and reported as off screen.
        }

        return Focus.Read(await page.EvaluateAsync<string>(FocusState));
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_submit_the_rules_refuse_focuses_the_first_invalid_field_and_scrolls_it_into_view(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;

        await page.ClickAsync("#rules-save");
        await Expect(page.Locator("#rules-email")).ToBeFocusedAsync();
        var focus = await FocusedAsync(page);

        Assert.Equal(new Focus("rules-email", true, true, "rules"), focus);
        await Expect(page.Locator("#rules-email")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#rules-city")).ToHaveAttributeAsync("aria-invalid", "true");
    }

    [Fact]
    public async Task A_submit_pressed_with_Enter_in_a_field_focuses_the_first_invalid_one()
    {
        await using var session = await OpenAsync(1280, 800);
        var page = session.Page;

        await page.FocusAsync("#rules-name");
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#rules-email")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task A_corrected_field_hands_the_next_refusal_to_the_field_still_wrong_and_an_accepted_save_moves_nothing()
    {
        await using var session = await OpenAsync(1280, 800);
        var page = session.Page;
        await page.ClickAsync("#rules-save");
        await Expect(page.Locator("#rules-email")).ToBeFocusedAsync();
        await page.FillAsync("#rules-email", "ada@example.com");

        await page.ClickAsync("#rules-save");
        await Expect(page.Locator("#rules-city")).ToBeFocusedAsync();
        await page.FillAsync("#rules-city", "Oslo");
        await page.ClickAsync("#rules-save");

        await Expect(page.Locator("#rules-said")).ToHaveTextAsync("saved Ada, Oslo");
        Assert.Equal("rules-save", await session.FocusAsync());
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_save_that_refuses_with_a_field_failure_focuses_the_field_it_names(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;

        await page.ClickAsync("#store-save");
        await Expect(page.Locator("#store-number")).ToBeFocusedAsync();
        var focus = await FocusedAsync(page);

        Assert.Equal(new Focus("store-number", true, true, "store"), focus);
        await Expect(page.Locator("#store-year")).Not.ToHaveAttributeAsync("aria-invalid", "true");
    }

    [Fact]
    public async Task A_reader_who_moves_to_another_field_while_the_save_is_in_flight_is_left_there()
    {
        await using var session = await OpenAsync(1280, 800);
        var page = session.Page;

        await page.ClickAsync("#slow-save");
        await page.ClickAsync("#elsewhere");
        await Expect(page.Locator("#slow-code")).ToHaveAttributeAsync("aria-invalid", "true", new() { Timeout = 10_000 });
        await page.Keyboard.TypeAsync("note");

        Assert.Equal("elsewhere", await session.FocusAsync());
        await Expect(page.Locator("#elsewhere")).ToHaveValueAsync("note");
    }

    [Fact]
    public async Task A_reader_who_waits_for_a_slow_save_is_taken_to_the_field_it_refused()
    {
        await using var session = await OpenAsync(1280, 800);
        var page = session.Page;

        await page.ClickAsync("#slow-save");

        await Expect(page.Locator("#slow-code")).ToBeFocusedAsync(new() { Timeout = 10_000 });
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_save_refused_inside_a_modal_focuses_the_field_in_the_modal_and_leaves_it_open(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;
        await page.ClickAsync("#rename");
        await Expect(page.Locator("dialog[data-rask-modal]")).ToBeVisibleAsync();
        await page.FillAsync("#modal-name", "Budapest");

        await page.ClickAsync("#modal-save");
        await Expect(page.Locator("#modal-note")).ToBeFocusedAsync();
        var focus = await FocusedAsync(page);

        Assert.Equal(new Focus("modal-note", true, true, "modal"), focus);
        Assert.True(await page.Locator("dialog[data-rask-modal]").EvaluateAsync<bool>("d => d.matches(':modal')"), "the refused save closed the modal");
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task A_refused_save_opens_the_tab_holding_the_first_invalid_field_and_focuses_it_once_shown(int width, int height)
    {
        await using var session = await OpenAsync(width, height);
        var page = session.Page;
        var tabs = page.Locator("#tabs");
        await tabs.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Notes" }).ClickAsync();

        await page.ClickAsync("#tabs-save");
        await Expect(page.Locator("#tabs-email")).ToBeFocusedAsync();
        var first = await FocusedAsync(page);
        await page.Keyboard.TypeAsync("ops@example.com");
        await page.ClickAsync("#tabs-save");
        await Expect(page.Locator("#tabs-street")).ToBeFocusedAsync();
        var second = await FocusedAsync(page);

        Assert.Equal(new Focus("tabs-email", true, true, "tabs"), first);
        Assert.Equal(new Focus("tabs-street", true, true, "tabs"), second);
        await Expect(tabs.GetByRole(Microsoft.Playwright.AriaRole.Tab, new() { Name = "Address" })).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Theory]
    [InlineData("select")]
    [InlineData("date")]
    [InlineData("pillbox")]
    public async Task A_kit_control_that_is_the_first_invalid_field_takes_focus_where_the_keyboard_works_it(string control)
    {
        await using var session = await OpenAsync(1280, 800);
        var page = session.Page;

        await page.ClickAsync($"#{control}-save");
        await page.WaitForFunctionAsync(
            "f => document.activeElement.closest('form') === document.getElementById(f) && document.activeElement.id !== f + '-save'",
            control);
        var focus = await FocusedAsync(page);

        Assert.True(focus.Invalid, $"focus went to {focus.Id}, which is no part of the invalid {control}");
        Assert.True(focus.OnScreen, $"the {control} was focused off screen");
        Assert.Equal(control, focus.Form);
    }
}

/// <summary>Forms that refuse: by their rules, by their save, slowly, in a modal, on tabs, and on kit controls.</summary>
public sealed partial class FirstInvalidPage : Component
{
    private readonly Contact _contact = new();
    private readonly Invoice _invoice = new();
    private readonly Invoice _slow = new();
    private readonly Renaming _renaming = new();
    private readonly Supplier _supplier = new();
    private readonly RoomChoice _room = new();
    private readonly DayChoice _day = new();
    private readonly TagChoice _tags = new();
    private string _said = "";
    private bool _open;

    protected override Component? HeadAssets => [Markup.Title["first invalid"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("rules-said")[_said],
        Input.Value(string.Empty).Id("elsewhere"),
        Form.Model(_contact).OnSubmit(c => _said = $"saved {c.Name}, {c.City}").Id("rules")[
            Ui.Input.Bind(() => _contact.Name).Label("Name").Id("rules-name"),
            // Far enough down that the field the refusal names has to be scrolled to.
            Div.Style("height:1400px"),
            Ui.Input.Bind(() => _contact.Email).Label("Email").Id("rules-email"),
            Ui.Input.Bind(() => _contact.City).Label("City").Id("rules-city"),
            Div.Style("height:1400px"),
            Ui.Button.Submit.Id("rules-save")["Save"]
        ],
        Form.Model(_invoice).OnSubmit(Store).Id("store")[
            Ui.Input.Bind(() => _invoice.Year).Label("Year").Id("store-year"),
            Ui.Input.Bind(() => _invoice.Number).Label("Number").Id("store-number"),
            Ui.Button.Submit.Id("store-save")["Save"]
        ],
        Form.Model(_slow).OnSubmit(StoreSlowly).Id("slow")[
            Ui.Input.Bind(() => _slow.Year).Label("Year").Id("slow-year"),
            Ui.Input.Bind(() => _slow.Number).Label("Code").Id("slow-code"),
            Ui.Button.Submit.Id("slow-save")["Save"]
        ],
        Button.Id("rename").OnClick(() => _open = true)["rename"],
        Ui.Modal.Open(_open).OnClose(() => _open = false)[
            Form.Model(_renaming).OnSubmit(Rename).Id("modal")[
                Ui.Input.Bind(() => _renaming.Name).Label("Name").Id("modal-name"),
                Ui.Input.Bind(() => _renaming.Note).Label("Note").Id("modal-note"),
                Ui.Button.Submit.Id("modal-save")["Rename"]
            ]
        ],
        Form.Model(_supplier).OnSubmit(_ => { }).Id("tabs")[
            Ui.FormTabs[
                Ui.FormTab.Name("details").Label("Details").Holds(() => _supplier.Name, () => _supplier.Email)[
                    Ui.Input.Bind(() => _supplier.Name).Label("Name").Id("tabs-name"),
                    Ui.Input.Bind(() => _supplier.Email).Label("Email").Id("tabs-email")
                ],
                Ui.FormTab.Name("address").Label("Address").Holds(() => _supplier.Street)[
                    Ui.Input.Bind(() => _supplier.Street).Label("Street").Id("tabs-street")
                ],
                Ui.FormTab.Name("notes").Label("Notes")[
                    Ui.Textarea.Bind(() => _supplier.Notes).Label("Notes").Id("tabs-notes")
                ]
            ],
            Ui.Button.Submit.Id("tabs-save")["Save"]
        ],
        Form.Model(_room).OnSubmit(_ => { }).Id("select")[
            Ui.Select.Bind(() => _room.Room).Label("Room").Listbox.Placeholder("Choose…")[
                Ui.SelectOption.Value("attic")["Attic"], Ui.SelectOption.Value("cellar")["Cellar"]
            ],
            Ui.Button.Submit.Id("select-save")["Save"]
        ],
        Form.Model(_day).OnSubmit(_ => { }).Id("date")[
            Ui.DatePicker.Bind(() => _day.Day).Label("Day"),
            Ui.Button.Submit.Id("date-save")["Save"]
        ],
        Form.Model(_tags).OnSubmit(_ => { }).Id("pillbox")[
            Ui.Pillbox.Bind(() => _tags.Tags).Label("Tags").Placeholder("Choose…")[
                Ui.PillboxOption["Alpha"], Ui.PillboxOption["Beta"]
            ],
            Ui.Button.Submit.Id("pillbox-save")["Save"]
        ]
    ];

    private static void Store(Invoice invoice) =>
        throw new RefusedException(new FieldFailure("That number is taken.", ["Number"]));

    private static async Task StoreSlowly(Invoice invoice)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        throw new RefusedException(new FieldFailure("That code is taken.", ["Number"]));
    }

    private void Rename(Renaming renaming)
    {
        if (string.Equals(renaming.Name, "Budapest", StringComparison.Ordinal))
        {
            throw new RefusedException(new FieldFailure("Say why it is renamed.", ["Note"]));
        }

        _open = false;
    }

    private sealed class Contact
    {
        [Required]
        public string Name { get; set; } = "Ada";

        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string City { get; set; } = "";
    }

    private sealed class Invoice
    {
        public string Year { get; set; } = "2026";

        public string Number { get; set; } = "42";
    }

    private sealed class Renaming
    {
        public string Name { get; set; } = "";

        public string Note { get; set; } = "";
    }

    private sealed class Supplier
    {
        [Required]
        public string Name { get; set; } = "Aminoff Freight";

        [Required]
        public string Email { get; set; } = "";

        [Required]
        public string Street { get; set; } = "";

        public string Notes { get; set; } = "";
    }

    private sealed class RoomChoice
    {
        [Required]
        public string? Room { get; set; }
    }

    private sealed class DayChoice
    {
        [Required]
        public DateOnly? Day { get; set; }
    }

    private sealed class TagChoice
    {
        [MinLength(1)]
        public List<string> Tags { get; set; } = [];
    }

    private sealed class RefusedException(params FieldFailure[] failures) : Exception("refused"), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures => failures;
    }
}
