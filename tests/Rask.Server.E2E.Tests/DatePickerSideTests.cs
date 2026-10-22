using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     An open date picker's calendar is on the side Flux's is, month by month. Reported from a form: a
///     <c>Ui.DatePicker</c> whose trigger ends 616 px down a 1440 × 950 window opens under it on October, and paged
///     to August, a month of six weeks, goes over it. Flux's live picker, held at the same place, does exactly that
///     (<c>scripts/flux/date-flip.mjs</c>, 2026-10-21): under it at 621 on the months of five weeks, over it at 213 on
///     August — the owner chose Flux's behaviour over keeping the side.
/// </summary>
public sealed class DatePickerSideTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Popup = "[data-ui-date-picker] [popover]";

    private const string Previous = Popup + " [data-rask-calendar-part=previous]";

    // Three frames on: a size change is answered in the next frame, and drawn in the one after.
    private const string Frames = "() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(done))))";

    // The popup's top and bottom, and where the previous-month arrow is, whole pixels.
    private const string Where = """
        () => {
            const p = document.querySelector('[data-ui-date-picker] [popover]').getBoundingClientRect();
            const a = document.querySelector('[data-ui-date-picker] [popover] [data-rask-calendar-part=previous]').getBoundingClientRect();
            return [p.top, p.bottom, a.x, a.y].map(Math.round).join(',');
        }
        """;

    private async Task<HookSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<DateFormPage>(
            playwright, new() { Locale = "hu-HU", ViewportSize = new() { Width = 1440, Height = 950 } },
            cultures: o => o.SupportedCultures.Add("hu"));
        await session.HooksLoadedAsync();

        // Scrolled as the form was: the trigger's bottom 616 px down the window.
        await session.Page.EvaluateAsync("() => window.scrollBy(0, document.querySelector('[data-ui-date-picker-button]').getBoundingClientRect().bottom - 616)");
        return session;
    }

    private static async Task<string> OpenedAsync(IPage page)
    {
        await page.ClickAsync("[data-ui-date-picker-button]");
        await Expect(page.Locator(Popup)).ToBeVisibleAsync();
        await page.EvaluateAsync(Frames);
        return await page.EvaluateAsync<string>(Where);
    }

    private static async Task<string> PagedAsync(IPage page, string day)
    {
        await page.ClickAsync(Previous);
        await Expect(page.Locator(Popup + $" td[data-date=\"{day}\"]:not([data-outside])")).ToHaveCountAsync(1);
        await page.EvaluateAsync(Frames);
        return await page.EvaluateAsync<string>(Where);
    }

    [Fact]
    public async Task Paged_from_october_to_july_the_calendar_is_on_the_side_flux_puts_it_each_month()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        var october = await OpenedAsync(page);
        var september = await PagedAsync(page, "2026-09-15");
        var august = await PagedAsync(page, "2026-08-15");
        var july = await PagedAsync(page, "2026-07-15");

        // Five weeks fit under the trigger (576..616); six do not, and go over it, 5 px clear of it, as Flux's.
        Assert.StartsWith("621,", october, StringComparison.Ordinal);
        Assert.StartsWith("621,", september, StringComparison.Ordinal);
        Assert.Equal("571", august.Split(',')[1]);
        Assert.StartsWith("621,", july, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_calendar_reopens_on_the_month_it_was_left_on_as_fluxs_does()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenedAsync(page);
        await PagedAsync(page, "2026-09-15");
        await PagedAsync(page, "2026-08-15");

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Popup)).ToBeHiddenAsync();
        await OpenedAsync(page);
        var month = await page.EvaluateAsync<string>("s => document.querySelector(s + ' td[data-date$=\"-15\"]:not([data-outside])').dataset.date", Popup);

        Assert.Equal("2026-08-15", month);
    }
}

/// <summary>The reported form: a bound, clearable Hungarian date picker with a label, far down the page.</summary>
public sealed partial class DateFormPage : Component
{
    private readonly Delivery _delivery = new();

    protected override Component? HeadAssets => [Markup.Title["date form"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        Div.Style("height:900px"),
        Form.Model(_delivery)[
            Ui.DatePicker.Bind(() => _delivery.Fulfilled).Locale("hu").Clearable(true).Label("Teljesítve")
        ],
        Div.Style("height:2000px")
    ];

    private sealed class Delivery
    {
        public DateOnly? Fulfilled { get; set; } = new(2026, 10, 15);
    }
}
