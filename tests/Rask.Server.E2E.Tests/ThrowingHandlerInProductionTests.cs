using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A list whose bin throws an exception with a secret in its message, in a real browser on a Production
///     host that wrote no error handling of its own: the reader keeps the list and its filter and is told in
///     a toast with a reference, and no word of the exception is in the page or in the frames that drew it.
///     A render that throws is still the error page, and <c>Try again</c> puts the app back as it was.
/// </summary>
public sealed partial class ThrowingHandlerInProductionTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_bin_that_throws_tells_a_hungarian_reader_in_a_toast_and_keeps_the_list_its_filter_and_the_next_click()
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<LeakingHandlerPage>(
            playwright,
            options: new() { ViewportSize = new() { Width = 1000, Height = 700 }, Locale = "hu-HU" },
            beforeLoad: page =>
            {
                frames = SocketFrames.Of(page);
                return Task.CompletedTask;
            },
            // The flag is the process's and the first host to map claims it: this host says so itself.
            beside: static _ => LiveOptions.IsDevelopment = false,
            environment: "Production",
            cultures: static c =>
            {
                c.SupportedCultures.Add("en");
                c.SupportedCultures.Add("hu");
            });
        var page = session.Page;
        await page.FillAsync("#filter", "ABC");
        await Expect(page.Locator("li")).ToHaveCountAsync(1);
        await page.ClickAsync("#count");
        await Expect(page.Locator("#clicks")).ToHaveTextAsync("clicks=1");

        await page.ClickAsync("#bin-ABC-123");

        var toast = page.Locator("[data-ui-toast-dialog]");
        await Expect(toast).ToContainTextAsync(HungarianWithReference());
        await Expect(toast).ToHaveAttributeAsync("data-variant", "danger");
        await Expect(page.Locator(".rask-error-boundary")).ToHaveCountAsync(0);
        await Expect(page.Locator("#row-ABC-123")).ToBeVisibleAsync();
        await Expect(page.Locator("#filter")).ToHaveValueAsync("ABC");
        await Expect(page.Locator("#attempts")).ToHaveTextAsync("attempts=1");
        var sent = await page.ContentAsync() + frames!.Replies();
        Assert.DoesNotContain("hunter2", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("db.internal", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeoutException", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("devError", sent, StringComparison.Ordinal);
        Assert.True(frames.FullPages.Count == 0, "The fault was answered with the whole document:" + Environment.NewLine + frames.Trace());
        await page.ClickAsync("#count");
        await Expect(page.Locator("#clicks")).ToHaveTextAsync("clicks=2");
    }

    [Fact]
    public async Task A_render_that_throws_shows_no_word_of_the_exception_and_try_again_puts_the_app_back()
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<LeakingHandlerPage>(
            playwright,
            beforeLoad: page =>
            {
                frames = SocketFrames.Of(page);
                return Task.CompletedTask;
            },
            beside: static _ => LiveOptions.IsDevelopment = false,
            environment: "Production");
        var page = session.Page;
        await page.ClickAsync("#count");
        await Expect(page.Locator("#clicks")).ToHaveTextAsync("clicks=1");

        await page.ClickAsync("#break");

        var shown = page.Locator(".rask-error-boundary");
        await Expect(shown.Locator("h1")).ToHaveTextAsync("Something went wrong");
        await Expect(shown).ToContainTextAsync("The problem has been recorded. If it keeps happening, quote this reference: ");
        var sent = await page.ContentAsync() + frames!.Replies();
        Assert.DoesNotContain("hunter2", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("db.internal", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeoutException", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("devError", sent, StringComparison.Ordinal);
        await shown.GetByText("Try again").ClickAsync();
        await Expect(shown).ToHaveCountAsync(0);
        await Expect(page.Locator("#clicks")).ToHaveTextAsync("clicks=1");
        await page.ClickAsync("#count");
        await Expect(page.Locator("#clicks")).ToHaveTextAsync("clicks=2");
    }

    [GeneratedRegex(
        @"Nem sikerült\. Kérjük, próbálja újra\. A hibát rögzítettük\. Ha újra előfordul, erre az azonosítóra hivatkozzon: [A-Z0-9]{8}",
        RegexOptions.None,
        1000)]
    private static partial Regex HungarianWithReference();
}

/// <summary>
///     A list as an app writes it — a filter, a bin on each row, the kit's toasts in the layout and no error
///     handling of its own — whose bin throws, and one of whose parts can be made to throw as it renders.
/// </summary>
public sealed partial class LeakingHandlerPage : Component
{
    private static readonly string[] Plates = ["ABC-123", "XYZ-789"];

    private string _filter = "";
    private int _clicks;
    private int _attempts;
    private bool _breaks;

    protected override Component? HeadAssets => Markup.Title["a handler that throws"];

    protected override Component? Render() => Div[Main[
        P.Id("clicks")[$"clicks={_clicks}"],
        P.Id("attempts")[$"attempts={_attempts}"],
        Button.Id("count").OnClick(() => _clicks++)["Count"],
        Button.Id("break").OnClick(() => _breaks = true)["Break"],
        Input.Value(_filter).Id("filter").OnInput(value => _filter = value),
        Ul[Plates.Where(plate => plate.Contains(_filter, StringComparison.OrdinalIgnoreCase)).Select(plate => (Component)Li.Key(plate).Id($"row-{plate}")[
            Span[plate],
            Button.Id($"bin-{plate}").OnClick(Delete)["Törlés"]
        ])],
        Broken()
    ], Ui.Toast
    ];

    // Throws the one time it is asked to, so the render Try again asks for draws the app again.
    private Component? Broken()
    {
        if (!_breaks)
        {
            return null;
        }

        _breaks = false;
        throw Leak();
    }

    private async Task Delete()
    {
        _attempts++;
        await Task.Delay(1);
        throw Leak();
    }

    private static InvalidOperationException Leak() =>
        new("Login failed for user 'sa' with password 'hunter2'.", new TimeoutException("db.internal:1433"));
}
