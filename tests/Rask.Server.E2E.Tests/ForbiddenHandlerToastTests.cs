using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A kit button whose handler carries <c>[Authorize(Roles = "admin")]</c>, clicked in a real browser by someone
///     who is not an admin: a toast says they may not, the row is still there, and the page goes on working.
/// </summary>
public sealed class ForbiddenHandlerToastTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData("#by-method-group")]
    [InlineData("#by-lambda")]
    public async Task A_handler_the_user_may_not_run_says_so_in_a_toast_and_changes_nothing(string button)
    {
        await using var session = await HookSession.OpenAsync<GuardedTrailersPage>(playwright, environment: "Production");
        var page = session.Page;
        await Expect(page.Locator("li")).ToHaveCountAsync(1);

        await page.ClickAsync(button);

        await Expect(page.GetByText("You do not have permission to do that.")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-ui-toast-dialog]")).ToHaveAttributeAsync("data-variant", "danger");
        await Expect(page.Locator("li")).ToHaveCountAsync(1);
        await Expect(page.Locator(".rask-error-boundary")).ToHaveCountAsync(0);
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("1");
    }
}

/// <summary>A list whose remove only an admin may run, on a page anyone may open.</summary>
public sealed partial class GuardedTrailersPage : Component
{
    private readonly List<string> _plates = ["ABC-123"];
    private int _counted;

    protected override Component? HeadAssets => Markup.Title["a handler the user may not run"];

    protected override Component? Render() => Div[Main[
        Ul[_plates.Select(static plate => (Component)Li.Key(plate)[plate])],
        Ui.Button.Danger.Id("by-method-group").OnClick(Remove)["Remove"],
        Ui.Button.Danger.Id("by-lambda").OnClick(() => Remove())["Remove"],
        Ui.Button.Id("count").OnClick(() => _counted++)["Count"],
        Span.Id("counted")[$"{_counted}"]
    ], Ui.Toast
    ];

    [Authorize(Roles = "admin")]
    private async Task Remove()
    {
        await Task.Delay(1);
        _plates.Clear();
    }
}
