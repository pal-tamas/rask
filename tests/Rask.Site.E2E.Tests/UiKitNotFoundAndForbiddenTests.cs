using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The UI tab's page for the built-in 404 and 403: <c>Ui.NotFound</c>, a warning callout with the way home, and
///     <c>Ui.Forbidden</c>, a danger callout a page draws in place of what the reader may not see.
/// </summary>
public sealed class UiKitNotFoundAndForbiddenTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task The_not_found_callout_says_its_sentence_and_leads_home() => RunAsync(async () =>
    {
        await OpenUiPageAsync("not-found", "Not found and forbidden");
        var callout = Page.Locator("[data-testid='ui-not-found'] [data-ui-callout]");
        await callout.ScrollIntoViewIfNeededAsync();

        var home = callout.GetByRole(AriaRole.Link, new() { Name = "Back to home" });

        await Expect(callout).ToContainTextAsync("The page you requested could not be found.");
        await Expect(home).ToHaveAttributeAsync("href", new Regex("/ui/?$"));
        var border = await callout.EvaluateAsync<string>("el => getComputedStyle(el).borderTopColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", border);
    });

    [Fact]
    public Task A_page_that_decides_for_itself_shows_forbidden_until_the_reader_may_see_it() => RunAsync(async () =>
    {
        await OpenUiPageAsync("not-found", "Not found and forbidden");
        var scope = Page.Locator("[data-testid='ui-forbidden']");
        await scope.ScrollIntoViewIfNeededAsync();
        await Expect(scope.GetByRole(AriaRole.Alert)).ToContainTextAsync("You do not have permission to view this page.");

        await scope.Locator("#uf-admin").ClickAsync();

        await Expect(scope.Locator("#uf-records")).ToHaveTextAsync("Three suppliers, 412 deliveries.", new() { Timeout = 10_000 });
        await Expect(scope.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
    });
}
