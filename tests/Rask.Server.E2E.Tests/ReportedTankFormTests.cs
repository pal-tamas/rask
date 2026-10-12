using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The app that reported a whole page a pick, rebuilt from its sources: a sidebar layout, a page that reads
///     what it edits, the editor as its keyed child, a listbox over a table of rows with mixed keyed cells.
/// </summary>
[Collection(PathBaseCollection.Name)]
public sealed class ReportedTankFormTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Trigger = "[data-ui-select-button]";

    private static ILocator Option(IPage page, string name) =>
        page.Locator("[data-ui-option]").Filter(new() { HasText = name });

    [Theory]
    [InlineData("tanks/new")]
    [InlineData("tanks/7")]
    [InlineData("why-in")]
    public async Task Every_pick_in_the_reported_tank_form_is_answered_with_a_diff(string path)
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<TankApp>(
            playwright, path: "/signin?to=/uj/" + path, pathBase: "/uj", environment: "Development", cookieSignIn: true, beside: SignsIn,
            beforeLoad: opened =>
            {
                frames = SocketFrames.Of(opened);
                return Task.CompletedTask;
            });
        var page = session.Page;
        await Expect(page.Locator("#ready")).ToHaveTextAsync("ready");
        frames!.Clear();

        if (path.StartsWith("tanks", StringComparison.Ordinal))
        {
            await page.GetByLabel("Megnevezés").FillAsync("Keleti");
            await page.GetByLabel("Méret (L)").FillAsync("600");
        }

        await page.ClickAsync(Trigger);
        await Expect(page.Locator(Trigger)).ToHaveAttributeAsync("aria-expanded", "true");
        await Option(page, "Glock").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("Glock");
        await Option(page, "Beretta").ClickAsync();
        await Expect(page.Locator(Trigger)).ToHaveTextAsync("2 selected");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Trigger)).ToHaveAttributeAsync("aria-expanded", "false");

        Assert.True(
            frames.FullPages.Count == 0,
            "A step was answered with the whole document:" + Environment.NewLine + frames.Trace());
    }

    // The older application's sign-in beside the app: a cookie for an administrator, then on to the page asked for.
    private static void SignsIn(WebApplication app) =>
        app.MapGet("/signin", async context =>
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Ada"), new Claim(ClaimTypes.Role, "admin")], LiveServerHost.CookieScheme);
            await context.SignInAsync(LiveServerHost.CookieScheme, new ClaimsPrincipal(identity));
            context.Response.Redirect(context.Request.Query["to"].ToString());
        });
}
