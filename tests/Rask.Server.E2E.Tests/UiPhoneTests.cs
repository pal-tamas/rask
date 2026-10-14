using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Phone</c> on a server-rendered page, typed into key by key: what the field shows and what its bound
///     value holds after every key are the ones fluxui.dev's phone field showed and held, recorded on its live page.
/// </summary>
/// <remarks>
///     Each row is a key, what the field then shows, and the value. The engine is the same release of
///     intl-tel-input Flux's page runs (29.2.3), so a formatting quirk is Flux's too: the space typed after
///     <c>+36</c> stays, an incomplete number is kept as typed, a seventeenth digit drops the formatting.
/// </remarks>
public sealed class UiPhoneTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // Recorded on fluxui.dev/components/phone, its first example (United States), one key at a time: what the
    // field showed, then the value it held; the keys' pairs are split by ";".
    public static TheoryData<string, string> Typed => new()
    {
        {
            "2015550123",
            "2|2;20|20;201|201;201-5|201-5;201-55|201-55;201-555|201-555;201-5550|+12015550;"
            + "(201) 555-01|(201) 555-01;(201) 555-012|(201) 555-012;(201) 555-0123|+12015550123"
        },
        {
            "201",
            "2|2;20|20;201|201"
        },
        {
            "+447400123456",
            "+|+;+4|+4;+44 |+44;+44 7|+44 7;+44 74|+44 74;+44 740|+44 740;+44 7400|+447400;"
            + "+44 7400 1|+4474001;+44 7400 12|+44740012;+44 7400 123|+447400123;+44 7400 1234|+4474001234;"
            + "+44 7400 12345|+44740012345;+44 7400 123456|+447400123456"
        },
        {
            "+36 30 123 4567",
            "+|+;+3|+3;+36 |+36;+36  |+36;+36 3 |+36 3;+36 30 |+36 30;+36 30  |+36 30;+36 30 1 |+36 30 1;"
            + "+36 30 12 |+36 30 12;+36 30 123 |+36 30 123;+36 30 123  |+36 30 123;+36 30 123 4 |+36301234;"
            + "+36 30 123 45 |+363012345;+36 30 123 456 |+3630123456;+36 30 123 4567 |+36301234567"
        },
        {
            "abc",
            "a|a;ab|ab;abc|abc"
        },
        {
            "06301234567",
            "0|0;06|06;063|063;0630|0630;06301|06301;063012|063012;0630123|+10630123;06301234|06301234;"
            + "063012345|063012345;0630123456|+10630123456;06301234567|06301234567"
        },
        {
            "(201) 555-0123 x12",
            "(|(;(2|(2;(20|(20;(201|(201;(201)|(201);(201) |(201);(201) 5|(201) 5;(201) 55|(201) 55;"
            + "(201) 555|(201) 555;(201) 555-|(201) 555-;(201) 555-0|+12015550;(201) 555-01|(201) 555-01;"
            + "(201) 555-012|(201) 555-012;(201) 555-0123|+12015550123;(201) 555-0123 |+12015550123;"
            + "(201) 555-0123 x|+12015550123;(201) 555-0123 x1|+12015550123;(201) 555-0123 x12|+12015550123"
        },
        {
            "12345678901234567",
            "1|1;12|12;1 23|1 23;1 (234|1 (234;1 (234) 5|1 (234) 5;1 (234) 56|1 (234) 56;"
            + "1 (234) 567|+11234567;1 (234) 567-8|1 (234) 567-8;1 (234) 567-89|1 (234) 567-89;"
            + "1 (234) 567-890|+11234567890;1 (234) 567-8901|+12345678901;123456789012|123456789012;"
            + "1234567890123|1234567890123;12345678901234|12345678901234;123456789012345|123456789012345;"
            + "1234567890123456|1234567890123456;12345678901234567|12345678901234567"
        },
    };

    [Theory]
    [MemberData(nameof(Typed))]
    public async Task Each_key_shows_and_holds_what_Fluxs_field_does(string keys, string expected)
    {
        await using var host = await LiveServerHost.StartAsync<PhonePage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        var field = page.Locator("#typed [data-ui-phone]");
        await Expect(field.Locator("input[type=tel]")).ToHaveAttributeAsync("data-intl-tel-input-id", new System.Text.RegularExpressions.Regex(".+"), new() { Timeout = 15_000 });
        var rows = Rows(expected);
        await field.Locator("input[type=tel]").ClickAsync();
        var seen = new List<string>();

        for (var at = 0; at < keys.Length; at++)
        {
            await page.Keyboard.TypeAsync(keys[at].ToString());
            await Expect(field.Locator("input[type=tel]")).ToHaveValueAsync(rows[at].Shown, new() { Timeout = 5_000 });
            seen.Add(await field.Locator("input[type=tel]").InputValueAsync() + "|" + await field.Locator("input[type=hidden]").InputValueAsync());
        }
        await page.Locator("#show").ClickAsync();

        Assert.Equal(rows.Select(row => row.Shown + "|" + row.Value), seen);
        await Expect(page.Locator("#value")).ToHaveTextAsync("value=" + rows[^1].Value, new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task A_number_typed_with_its_code_picks_the_country_and_the_button_says_so()
    {
        await using var host = await LiveServerHost.StartAsync<PhonePage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        var field = page.Locator("#typed [data-ui-phone]");
        var button = field.Locator("button[role=combobox]");
        await Expect(field.Locator("input[type=tel]")).ToHaveAttributeAsync("data-intl-tel-input-id", new System.Text.RegularExpressions.Regex(".+"), new() { Timeout = 15_000 });

        await field.Locator("input[type=tel]").ClickAsync();
        await page.Keyboard.TypeAsync("+4");
        var none = await button.GetAttributeAsync("aria-label");
        await page.Keyboard.TypeAsync("4");

        Assert.Equal("Change country", none);
        await Expect(button).ToHaveAttributeAsync("aria-label", "Change country: United Kingdom (+44)");
        await Expect(button.Locator("[data-value]")).ToHaveAttributeAsync("data-value", "gb");
        await Expect(button.Locator("[data-country-code]")).ToHaveTextAsync("+44");
    }

    [Fact]
    public async Task The_list_opens_on_its_search_filters_and_a_pick_keeps_the_number_and_reads_it_again()
    {
        await using var host = await LiveServerHost.StartAsync<PhonePage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        var field = page.Locator("#typed [data-ui-phone]");
        var tel = field.Locator("input[type=tel]");
        var button = field.Locator("button[role=combobox]");
        await Expect(tel).ToHaveAttributeAsync("data-intl-tel-input-id", new System.Text.RegularExpressions.Regex(".+"), new() { Timeout = 15_000 });
        await tel.ClickAsync();
        await page.Keyboard.TypeAsync("2015550123");

        await button.ClickAsync();
        var search = field.Locator("[popover] input");
        await Expect(search).ToBeFocusedAsync();
        await Expect(field.Locator("[role=option]:not([data-hidden])")).ToHaveCountAsync(244);
        await Expect(field.Locator("[role=option][data-active]")).ToHaveAttributeAsync("value", "us");
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(field.Locator("[role=option][data-active]")).ToHaveAttributeAsync("value", "uy");
        await page.Keyboard.TypeAsync("36");
        await Expect(field.Locator("[role=option]:not([data-hidden])")).ToHaveCountAsync(2);
        await page.Keyboard.PressAsync("ControlOrMeta+a");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.TypeAsync("hung");
        await page.Keyboard.PressAsync("Enter");

        await Expect(field.Locator("[popover]")).Not.ToBeVisibleAsync();
        await Expect(tel).ToBeFocusedAsync();
        await Expect(button).ToHaveAttributeAsync("aria-label", "Change country: Hungary (+36)");
        await Expect(tel).ToHaveValueAsync("(201) 555-0123");
        await Expect(field.Locator("input[type=hidden]")).ToHaveValueAsync("(201) 555-0123");
    }

    [Fact]
    public async Task A_value_the_page_gives_is_shown_in_its_own_countrys_way()
    {
        await using var host = await LiveServerHost.StartAsync<PhonePage>(blockWebSockets: false, staticFiles: true);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        var office = page.Locator("#office [data-ui-phone]");

        await Expect(office.Locator("input[type=tel]")).ToHaveValueAsync("020 7183 8750", new() { Timeout = 15_000 });
        await page.Locator("#set").ClickAsync();

        await Expect(office.Locator("input[type=tel]")).ToHaveValueAsync("030 1234567", new() { Timeout = 15_000 });
        await Expect(office.Locator("button[role=combobox]")).ToHaveAttributeAsync("aria-label", "Change country: Germany (+49)");
    }

    private static List<(string Shown, string Value)> Rows(string expected) =>
        [.. expected.Split(';').Select(row => row.Split('|')).Select(pair => (pair[0], pair[1]))];
}

public sealed partial class PhonePage : Component
{
    private readonly PhoneContact _contact = new();
    private string? _shown;

    protected override Component? HeadAssets => Markup.Title["phone"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Div.Id("typed")[Ui.Phone.Bind(() => _contact.Mobile).Country("US").Label("Phone number")],
        Div.Id("office")[Ui.Phone.Bind(() => _contact.Office).Country("GB").Label("Office phone")],
        Button.Id("show").OnClick(() => _shown = _contact.Mobile)["show"],
        Button.Id("set").OnClick(() => _contact.Office = "+49301234567")["set"],
        P.Id("value")[$"value={_shown}"]
    ];
}

/// <summary>What the page binds its phone fields to: properties, as a form model has.</summary>
public sealed class PhoneContact
{
    public string? Mobile { get; set; }

    public string? Office { get; set; } = "+442071838750";
}
