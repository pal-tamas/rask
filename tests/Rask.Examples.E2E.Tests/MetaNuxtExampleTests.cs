using System.Net;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The meta framework lane, end to end: Kestrel on the public port, Nuxt's own Node server
///     supervised beside it, and a page that dispatches into C# through the generated wire.
/// </summary>
/// <remarks>
///     Everything else about this lane is asserted on artifacts — pack items, targets text, a published
///     tree. Those can all be right while the running app is wrong, and were: the alias the whole lane
///     is built around resolved to nothing on three of the six frameworks, and every structural test
///     still passed. This is the test that could not have.
/// </remarks>
[Collection(MetaNuxtExampleCollection.Name)]
public sealed class MetaNuxtExampleTests(MetaNuxtAppFixture app, PlaywrightFixture playwright)
{
    [Fact]
    public async Task The_greeting_is_rendered_by_node_before_any_script_runs()
    {
        // Fetched, not driven: no browser, no JavaScript. If the text is in this response it was
        // produced by Nuxt inside the supervised Node process, from a dispatch that went back to
        // Kestrel and into the C# handler — the one claim this lane makes that the SPA lane cannot.
        using var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl) };

        var response = await http.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hello, meta!", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_answers_on_the_same_origin_rather_than_being_forwarded()
    {
        // Kestrel owns the port and answers its own routes; everything else is forwarded to node.
        // Getting that order backwards is this lane's characteristic failure — an API call answered
        // with a rendered page — and it looks like a front-end bug when it happens.
        using var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl) };

        var health = await http.GetAsync("/healthz");
        var body = await health.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", body.Trim());
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_page_hydrates_and_a_command_round_trips_from_the_browser()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(app.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // The server-rendered half survived hydration rather than being blanked by it.
        await Assertions.Expect(page.GetByTestId("greeting-message")).ToHaveTextAsync("Hello, meta!");

        // The browser layer, imported as `@rask/browser/mediaQuery` by the page. It answers only after
        // mount — during a Node render there is no media query to ask — so this also proves the module
        // survived the server pass without touching `window` at import time.
        await Assertions.Expect(page.GetByTestId("prefers-dark")).ToContainTextAsync("prefers dark:");

        // A command over the same wire, from the browser this time: POST, because the C# record
        // implements ICommand and the verb comes from the type rather than from a call site.
        await Assertions.Expect(page.GetByTestId("visits")).ToHaveTextAsync("not yet");
        await page.GetByTestId("visit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("visits")).ToContainTextAsync("visits: 1");
    }
}
