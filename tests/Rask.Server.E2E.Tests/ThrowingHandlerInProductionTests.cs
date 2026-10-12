using Rask.Core;
using Rask.Core.Live;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A button whose handler throws an exception with a secret in its message, in a real browser on a
///     Production host: the visitor gets the error page with a reference and no word of the exception, in
///     the page and in the frames that drew it, and <c>Try again</c> puts the app back as it was.
/// </summary>
public sealed class ThrowingHandlerInProductionTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_handler_that_throws_shows_no_word_of_the_exception_and_try_again_puts_the_app_back()
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<LeakingHandlerPage>(
            playwright,
            beforeLoad: page =>
            {
                frames = SocketFrames.Of(page);
                return Task.CompletedTask;
            },
            // The flag is the process's and the first host to map claims it: this host says so itself.
            beside: static _ => LiveOptions.IsDevelopment = false,
            environment: "Production");
        var page = session.Page;
        await page.ClickAsync("#count");
        await Expect(page.Locator("#clicks")).ToHaveTextAsync("clicks=1");

        await page.ClickAsync("#fail");

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
}

/// <summary>A page as an app writes it, with no error handling of its own, one of whose handlers throws.</summary>
public sealed partial class LeakingHandlerPage : Component
{
    private int _clicks;

    protected override Component? HeadAssets => Markup.Title["a handler that throws"];

    protected override Component? Render() => Div[Main[
        P.Id("clicks")[$"clicks={_clicks}"],
        Button.Id("count").OnClick(() => _clicks++)["Count"],
        Button.Id("fail").OnClick(Fail)["Fail"]
    ]];

    private static void Fail() =>
        throw new InvalidOperationException(
            "Login failed for user 'sa' with password 'hunter2'.", new TimeoutException("db.internal:1433"));
}
