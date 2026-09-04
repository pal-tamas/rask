using System.Net;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The meta lane on SvelteKit, and the one journey that would have caught the alias bug.
/// </summary>
/// <remarks>
///     SvelteKit generates the tsconfig its app is checked against, from <c>kit.alias</c>. Rask's
///     first attempt wrote <c>@rask/*</c> into the app's own <c>paths</c> instead, which silently
///     DISPLACED the generated <c>$lib</c> mapping — imports the developer never touched stopped
///     resolving, and every artifact test still passed. The page here imports both, so that cannot
///     come back quietly.
/// </remarks>
[Collection(MetaSvelteKitExampleCollection.Name)]
public sealed class MetaSvelteKitExampleTests(MetaSvelteKitAppFixture app, PlaywrightFixture playwright)
{
    [Fact]
    public async Task The_greeting_is_rendered_by_node_before_any_script_runs()
    {
        using var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl) };

        var response = await http.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hello, meta!", html, StringComparison.Ordinal);

        // From $lib, which Rask's alias must not have displaced. In the first response, so this is
        // the server render's copy rather than anything hydration put back.
        Assert.Contains("From C#, during the server render", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_answers_on_the_same_origin_rather_than_being_forwarded()
    {
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

        await Assertions.Expect(page.GetByTestId("greeting-message")).ToHaveTextAsync("Hello, meta!");
        await Assertions.Expect(page.GetByTestId("visits")).ToHaveTextAsync("not yet");

        await page.GetByTestId("visit").ClickAsync();

        await Assertions.Expect(page.GetByTestId("visits")).ToContainTextAsync("visits: 1");
        await Assertions.Expect(page.GetByTestId("prefers-dark")).ToContainTextAsync("prefers dark:");
    }
}
