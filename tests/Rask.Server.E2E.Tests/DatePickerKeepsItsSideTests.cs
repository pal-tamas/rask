using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     An open panel keeps the side it opened on while it changes size — a departure from Flux, which flips it
///     (<c>scripts/flux/date-flip.mjs</c>). Reported from a form: a <c>Ui.DatePicker</c> whose trigger ends 616 px
///     down a 1440 × 950 window opens under it on October, and paged to August, a month of six weeks, it jumped
///     over the trigger, so the arrow being pressed ran away from the pointer.
/// </summary>
public sealed class DatePickerKeepsItsSideTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
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
    public async Task Paged_from_october_to_august_and_back_the_open_calendar_stays_under_its_trigger_and_its_arrow_under_the_pointer()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        var october = await OpenedAsync(page);
        var september = await PagedAsync(page, "2026-09-15");
        var august = await PagedAsync(page, "2026-08-15");
        // Scrolled to its end, the last of August is inside the window.
        var lastDay = await page.EvaluateAsync<int>("s => { const p = document.querySelector(s); p.scrollTop = p.scrollHeight; return Math.round(p.querySelector('td[data-date=\"2026-08-31\"]').getBoundingClientRect().bottom); }", Popup);
        var july = await PagedAsync(page, "2026-07-15");

        Assert.StartsWith("621,", october, StringComparison.Ordinal);
        var arrow = october.Split(',')[2..];
        Assert.All(new[] { september, august, july }, at =>
        {
            Assert.StartsWith("621,", at, StringComparison.Ordinal);
            Assert.Equal(arrow, at.Split(',')[2..]);
        });

        // Six weeks are taller than the room under the trigger: held to it, inside the window, and scrolled.
        Assert.InRange(int.Parse(august.Split(',')[1], System.Globalization.CultureInfo.InvariantCulture), 940, 950);
        Assert.InRange(lastDay, 621, 950);
    }

    [Fact]
    public async Task A_scroll_chooses_the_side_again_and_the_calendar_reopens_on_the_month_it_was_left_on()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenedAsync(page);
        await PagedAsync(page, "2026-09-15");
        await PagedAsync(page, "2026-08-15");

        await page.EvaluateAsync("() => window.scrollBy(0, 1)");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-ui-date-picker] [popover]').getBoundingClientRect().bottom <= document.querySelector('[data-ui-date-picker-button]').getBoundingClientRect().top");
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
