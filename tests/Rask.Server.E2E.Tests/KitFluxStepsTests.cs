using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Three of Flux's steps in a real browser: an amount grouped as it is typed and read back as a number
///     (<c>.Money()</c>), the OTP form's message centred with no icon, and a listbox whose rows are checkboxes.
/// </summary>
public sealed class KitFluxStepsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static async Task<HookSession> OpenAsync(PlaywrightFixture playwright)
    {
        var session = await HookSession.OpenAsync<KitFluxStepsPage>(playwright);
        await session.HooksLoadedAsync();
        await session.Page.ClickAsync("#ready");
        await Expect(session.Page.Locator("#is-ready")).ToHaveTextAsync("ready");
        return session;
    }

    [Fact]
    public async Task An_amount_is_grouped_as_it_is_typed_and_the_bound_decimal_receives_the_number()
    {
        await using var session = await OpenAsync(playwright);
        var page = session.Page;

        await page.Locator("#amount").FillAsync("");
        await page.Locator("#amount").PressSequentiallyAsync("1234567");
        await Expect(page.Locator("#amount")).ToHaveValueAsync("1,234,567");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=1234567|1234.56");
    }

    [Fact]
    public async Task An_amount_with_marks_of_its_own_is_shown_and_read_back_with_them()
    {
        await using var session = await OpenAsync(playwright);
        var page = session.Page;
        var shown = await page.Locator("#local").InputValueAsync();

        await page.Locator("#local").FillAsync("");
        await page.Locator("#local").PressSequentiallyAsync("98765,4");
        await Expect(page.Locator("#local")).ToHaveValueAsync("98 765,4");
        await page.ClickAsync("#save");

        Assert.Equal("1 234,56", shown);
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=0|98765.4");
    }

    [Fact]
    public async Task The_codes_message_is_centred_under_the_cells_with_no_icon()
    {
        await using var session = await OpenAsync(playwright);
        var page = session.Page;

        await page.ClickAsync("#verify");

        var error = page.Locator("#otp [data-ui-error]");
        await Expect(error).ToBeVisibleAsync();
        await Expect(error).ToHaveCSSAsync("text-align", "center");
        await Expect(error.Locator("svg")).ToHaveCountAsync(0);
        await Expect(page.Locator("#otp label")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\bsr-only\\b"));
    }

    [Fact]
    public async Task A_code_typed_and_not_yet_written_is_emptied_by_a_handler_that_empties_the_property()
    {
        await using var session = await OpenAsync(playwright);
        var page = session.Page;
        var cells = page.Locator("#resend-otp input[data-ui-otp-input]");

        await cells.Nth(0).ClickAsync();
        await page.Keyboard.TypeAsync("4321");
        await Expect(cells.Nth(3)).ToHaveValueAsync("1");
        await page.ClickAsync("#resend");

        await Expect(page.Locator("#resent")).ToHaveTextAsync("resent=4321");
        await Expect(cells.Nth(0)).ToHaveValueAsync(string.Empty);
        await Expect(cells.Nth(3)).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task Two_rows_picked_in_a_checkbox_listbox_tick_both_boxes()
    {
        await using var session = await OpenAsync(playwright);
        var page = session.Page;

        await page.ClickAsync("#industries [data-ui-select-button]");
        await Expect(page.Locator("#industries [popover]")).ToBeVisibleAsync();
        await page.Locator("#industries [data-ui-option]").Filter(new() { HasText = "Photography" }).ClickAsync();
        await page.Locator("#industries [data-ui-option]").Filter(new() { HasText = "Legal services" }).ClickAsync();

        var ticked = page.Locator("#industries [data-ui-option][data-selected]");
        await Expect(ticked).ToHaveCountAsync(2);
        await Expect(ticked.Locator("svg")).ToHaveCountAsync(2);
        await Expect(ticked.Nth(0).Locator("svg")).ToBeVisibleAsync();
        await Expect(ticked.Nth(1).Locator("svg")).ToBeVisibleAsync();
        await Expect(page.Locator("#industries [data-ui-option]:not([data-selected]) svg").First).ToBeHiddenAsync();
    }
}

/// <summary>The amounts, the code and the listbox, bound to the properties of one model.</summary>
public sealed partial class KitFluxStepsPage : Component
{
    private readonly Payment _payment = new();
    private readonly Verification _verification = new();
    private string _saved = "";
    private bool _ready;
    private string _resent = "";

    /// <summary>The code the page itself holds, as a Livewire component's property: Flux's <c>wire:model="code"</c>.</summary>
    public string Code { get; set; } = "";

    protected override Component? HeadAssets => [Markup.Title["kit flux steps"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Markup.Button.Id("ready").OnClick(() => _ready = true)["Ready"],
        P.Id("is-ready")[_ready ? "ready" : "waiting"],
        P.Id("saved")[$"saved={_saved}"],
        Form.Model(_payment).OnSubmit(Save)[
            Ui.Input.Bind(() => _payment.Amount).Label("Amount").Id("amount").Money(),
            Ui.Input.Bind(() => _payment.Local).Label("Local").Id("local").Money(decimals: ",", thousands: " "),
            Ui.Button.Submit.Id("save")["Save"]
        ],
        Div.Id("otp")[
            Form.Model(_verification)[
                Ui.Otp.Bind(() => _verification.Code).Length(6).Label("OTP Code").LabelSrOnly().ErrorIcon(false).ErrorClass("text-center").Class("mx-auto"),
                Ui.Button.Submit.Id("verify")["Verify"]
            ]
        ],
        Div.Id("resend-otp")[
            Ui.Otp.Bind(() => Code).Length(6).Label("Resend code"),
            Markup.Button.Id("resend").OnClick(() => { _resent = Code; Code = ""; })["Resend"],
            P.Id("resent")[$"resent={_resent}"]
        ],
        Div.Id("industries")[
            Ui.Select.Bind(() => _payment.Industries).Listbox.Checkbox.Multiple().Placeholder("Choose industries...")[
                Ui.SelectOption["Photography"], Ui.SelectOption["Design services"], Ui.SelectOption["Legal services"]
            ]
        ]
    ];

    private void Save(Payment payment) =>
        _saved = string.Create(CultureInfo.InvariantCulture, $"{payment.Amount}|{payment.Local}");

    private sealed class Payment
    {
        public decimal Amount { get; set; }

        public decimal Local { get; set; } = 1234.56m;

        public List<string> Industries { get; set; } = [];
    }

    private sealed class Verification
    {
        [Required]
        public string Code { get; set; } = "";
    }
}
