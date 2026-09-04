using System.Net;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The meta lane on SolidStart — the framework whose bundler resolves <c>@rask/*</c> through a
///     Vite alias rather than through a tsconfig.
/// </summary>
/// <remarks>
///     A tsconfig <c>paths</c> entry is a type-checking concept; Vite does not read one. SolidStart's
///     config declares no <c>resolve</c> of its own, so Rask adds the alias — and without it the
///     imports type-check and then fail the BUILD with "Failed to resolve import". That is the order
///     this journey exists to keep honest: the build passing is not the same as the page working.
/// </remarks>
[Collection(MetaSolidStartExampleCollection.Name)]
public sealed class MetaSolidStartExampleTests(MetaSolidStartAppFixture app, PlaywrightFixture playwright)
{
    [Fact]
    public async Task The_greeting_is_rendered_by_node_before_any_script_runs()
    {
        // Waits out the documented startup window: Kestrel binds before the node child is
        // listening, and forwards are answered 503 with a Retry-After until it is.
        var html = await MetaFrontEnd.WaitForPageAsync(app.BaseUrl);

        Assert.Contains("Hello, meta!", html, StringComparison.Ordinal);
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
        await MetaFrontEnd.WaitForPageAsync(app.BaseUrl);

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
