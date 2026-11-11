using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     TA's supply editor (nightly .3033): two pickers over the same seven rows the editor reads in its
///     <c>OnMount</c>, keyed after <c>.Choices(rows)</c> as TA's <c>Picker</c> keys them — one <c>.Live()</c> with an
///     <c>AfterBind</c>, one not. Each opens on all seven, picks one, and opens on all seven again; the live one opened
///     on "Nincs találat" with no row at all, because the rows read after the first render landed on the instance the
///     key set aside.
/// </summary>
public sealed class ChoicesReadAfterMountTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string, int> Readers => new()
    {
        { "en-US", 1280 }, { "en-US", 390 }, { "hu-HU", 1280 }, { "hu-HU", 390 },
    };

    private static ILocator Options(IPage page, string box) => page.Locator($"#{box} [role=option]:visible");

    private static async Task OpenAsync(IPage page, string box)
    {
        await page.ClickAsync($"#{box} [data-ui-select-button]");
        await Expect(page.Locator($"#{box} [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "true");
    }

    private static async Task ListsEveryRowAsync(IPage page, string box)
    {
        await OpenAsync(page, box);
        await Expect(Options(page, box)).ToHaveCountAsync(ChoicesEditor.Rows.Length);
        await Expect(page.Locator($"#{box} [data-ui-listbox-empty]")).ToBeHiddenAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator($"#{box} [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "false");
    }

    private async Task<HookSession> EditorAsync(string locale, int width)
    {
        var session = await HookSession.OpenAsync<ChoicesEditorApp>(
            playwright, new() { Locale = locale, ViewportSize = new() { Width = width, Height = 800 } },
            path: "/choices-after-mount", cultures: o => o.SupportedCultures.Add("hu"));
        await session.Page.ClickAsync("#new");
        await Expect(session.Page.Locator("#read")).ToHaveTextAsync("7");

        return session;
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public async Task A_live_picker_lists_every_row_picks_one_runs_its_after_bind_and_lists_every_row_again(string locale, int width)
    {
        await using var session = await EditorAsync(locale, width);
        var page = session.Page;

        await ListsEveryRowAsync(page, "live");
        await OpenAsync(page, "live");
        await Options(page, "live").Filter(new() { HasText = "Gamma Bt." }).ClickAsync();

        await Expect(page.Locator("#company")).ToHaveTextAsync("3");
        await Expect(page.Locator("#after")).ToHaveTextAsync("1");
        await Expect(page.Locator("#live [data-ui-select-button]")).ToContainTextAsync("Gamma Bt.");
        await ListsEveryRowAsync(page, "live");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public async Task A_picker_that_waits_for_the_next_action_lists_every_row_and_hands_over_its_pick(string locale, int width)
    {
        await using var session = await EditorAsync(locale, width);
        var page = session.Page;

        await ListsEveryRowAsync(page, "held");
        await OpenAsync(page, "held");
        await Options(page, "held").Filter(new() { HasText = "Éta Kft." }).ClickAsync();
        await page.ClickAsync("#check");

        await Expect(page.Locator("#supplier")).ToHaveTextAsync("7");
        await Expect(page.Locator("#held [data-ui-select-button]")).ToContainTextAsync("Éta Kft.");
        await ListsEveryRowAsync(page, "held");
        Assert.Empty(session.PageErrors);
    }
}

/// <summary>The app: its router.</summary>
public sealed partial class ChoicesEditorApp : Component
{
    protected override Component? HeadAssets => [Markup.Title["choices"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override Component? Render() => Router;
}

/// <summary>The list the editor opens from, as TA's supplies page opens its editor.</summary>
[Route("/choices-after-mount")]
public sealed partial class ChoicesAfterMountPage : Component
{
    private bool _editing;

    protected override Component? Render() => Main[
        Ui.Button.Id("new").OnClick(() => _editing = true)["New supply"],
        _editing ? ChoicesEditor : null
    ];
}

/// <summary>TA's <c>SupplyEditor</c>: the rows read in <c>OnMount</c>, after an await, and a picker over them each.</summary>
public sealed partial class ChoicesEditor : Component
{
    internal static readonly Ui.Choice<int>[] Rows =
    [
        new(1, "Alfa Kft."), new(2, "Béta Zrt."), new(3, "Gamma Bt."), new(4, "Delta Kft."),
        new(5, "Epszilon Zrt."), new(6, "Zéta Bt."), new(7, "Éta Kft."),
    ];

    private readonly Supply _form = new();
    private Ui.Choice<int>[] _companies = [];
    private int _afterBinds;

    protected override async Task OnMount()
    {
        await Task.Delay(150);
        _companies = Rows;
    }

    protected override Component? Render() =>
    [
        Form.Model(_form)[
            Div.Id("live")[
                Ui.Field[
                    Ui.Label["Company"],
                    Ui.Select.Bind(() => _form.CompanyId).Choices(_companies)
                        .Key("company").Live().AfterBind(_ => _afterBinds++).Listbox.Searchable(true)
                ]
            ],
            Div.Id("held")[
                Ui.Field[
                    Ui.Label["Supplier"],
                    Ui.Select.Bind(() => _form.SupplierId).Choices(_companies).Key("supplier").Listbox.Searchable(true)
                ]
            ]
        ],
        Ui.Button.Id("check").OnClick(() => { })["Check"],
        P.Id("read")[_companies.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)],
        P.Id("company")[_form.CompanyId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"],
        P.Id("supplier")[_form.SupplierId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"],
        P.Id("after")[_afterBinds.ToString(System.Globalization.CultureInfo.InvariantCulture)]
    ];

    /// <summary>The supply the editor writes.</summary>
    public sealed class Supply
    {
        public int? CompanyId { get; set; }

        public int? SupplierId { get; set; }
    }
}
