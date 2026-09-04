using System.Net;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The meta lane on Next.js — the same claims as the Nuxt journey, against the other server shape
///     and the other rendering model.
/// </summary>
/// <remarks>
///     Worth running twice rather than trusting one framework to speak for six. Next is the one whose
///     standalone output omits its own static assets, the one that reads <c>HOSTNAME</c> where the
///     others read <c>HOST</c>, and the one that prerenders a server component at BUILD time unless
///     the page opts out — a dispatch to a host that is not running yet, which is a build failure the
///     other five do not have.
/// </remarks>
[Collection(MetaNextExampleCollection.Name)]
public sealed class MetaNextExampleTests(MetaNextAppFixture app, PlaywrightFixture playwright)
{
    [Fact]
    public async Task The_greeting_is_rendered_by_node_before_any_script_runs()
    {
        using var http = new HttpClient { BaseAddress = new Uri(app.BaseUrl) };

        var response = await http.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hello, meta!", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_static_assets_next_omits_from_standalone_are_served_by_kestrel()
    {
        // Next's standalone output deliberately leaves out `.next/static`, assuming a CDN in front.
        // Here Kestrel is that thing and serves them from the publish tree — so a page whose scripts
        // 404 is exactly what this lane exists to prevent, and it would read as a hydration bug rather
        // than a hosting one.
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var failures = new List<string>();
        page.Response += (_, response) =>
        {
            if (response.Status >= 400 && response.Url.Contains("/_next/", StringComparison.Ordinal))
            {
                lock (failures)
                {
                    failures.Add($"{response.Status} {response.Url}");
                }
            }
        };

        await page.GotoAsync(app.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.Empty(failures);
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
