using System.Net;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The meta lane on Analog, and the journey that makes its vendored client real.
/// </summary>
/// <remarks>
///     <para>
///         Analog is the one sample whose front end is vendored into this repository rather than pulled
///         by its creator, because <c>create-analog</c> runs <c>git init</c> and the embedded repository
///         that leaves behind means none of its files are committed. Vendoring solves that and
///         introduces a different risk: files that nothing builds and nothing runs, rotting quietly.
///         This journey is the only thing that compiles the vendored client and drives it.
///     </para>
///     <para>
///         <b>It deliberately does not assert a server render, unlike the other five.</b> Asked
///         directly on its own port, this app's Nitro server returns the client shell with no component
///         markup in it — so the page is rendered in the browser, and Rask is forwarding faithfully.
///         Asserting SSR here would be asserting a behaviour the sample does not have, and would fail
///         pointing at the meta lane rather than at the front end's own configuration. Tracked
///         separately; what this journey pins is everything the lane is actually responsible for.
///     </para>
/// </remarks>
[Collection(MetaAnalogExampleCollection.Name)]
public sealed class MetaAnalogExampleTests(MetaAnalogAppFixture app, PlaywrightFixture playwright)
{
    [Fact]
    public async Task The_document_comes_from_node_rather_than_from_kestrel()
    {
        // Waits out the documented startup window: Kestrel binds before the node child is
        // listening, and forwards are answered 503 with a Retry-After until it is.
        var html = await MetaFrontEnd.WaitForPageAsync(app.BaseUrl);

        // Analog's own document, forwarded: its <base href> and its hashed entry bundle. Kestrel has
        // no page of its own that looks like this, so this is the forward working end to end.
        Assert.Contains("<base href=\"/\">", html, StringComparison.Ordinal);
        Assert.Contains("/assets/", html, StringComparison.Ordinal);
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
    public async Task The_page_renders_and_a_command_round_trips_from_the_browser()
    {
        await MetaFrontEnd.WaitForPageAsync(app.BaseUrl);

        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        // A module specifier that does not resolve is a CONSOLE error and nothing else: the page still
        // returns 200, the markup is still there, and only the component never appears. That is exactly
        // how the @rask/* alias went missing from Analog's bundle, so it is asserted rather than left to
        // be inferred from a locator timing out.
        var consoleErrors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") { consoleErrors.Add(m.Text); } };
        page.PageError += (_, e) => consoleErrors.Add(e);

        await page.GotoAsync(app.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.True(
            consoleErrors.Count == 0,
            "the page reported console errors: " + string.Join(" | ", consoleErrors));

        // The generated wire resolving under Analog's build — the strictest TypeScript of the six —
        // is the whole point of this sample, and this is what proves it at runtime rather than at
        // compile time. The greeting is dispatched from the component, so it arrives after the page.
        await Assertions.Expect(page.GetByTestId("greeting-message")).ToHaveTextAsync("Hello, meta!");
        await Assertions.Expect(page.GetByTestId("visits")).ToHaveTextAsync("not yet");

        await page.GetByTestId("visit").ClickAsync();

        await Assertions.Expect(page.GetByTestId("visits")).ToContainTextAsync("visits: 1");
        await Assertions.Expect(page.GetByTestId("prefers-dark")).ToContainTextAsync("prefers dark:");
    }
}
