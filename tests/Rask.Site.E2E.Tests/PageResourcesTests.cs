using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Every resource a page of the published site asks for comes back (#1349): a missing image, font or script still
///     leaves the page drawn whole, so only a listener on the responses notices it.
/// </summary>
/// <remarks>
///     The landing page, the docs, and the app frame both as the UI page that frames it and as its demo drawn whole: a page
///     each of the site's three layouts, and the one framed demo. The harness aborts the font CDNs on purpose, so a
///     failure there is not a miss.
/// </remarks>
public sealed class PageResourcesTests(WasmExampleAppFixture app, PlaywrightFixture pw) : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData("/", 1280, 800)]
    [InlineData("/", 390, 844)]
    [InlineData("/docs/", 1280, 800)]
    [InlineData("/docs/", 390, 844)]
    [InlineData("/ui/app-frame/", 1280, 800)]
    [InlineData("/ui/app-frame/", 390, 844)]
    [InlineData("/demo/app-frame/", 1280, 800)]
    [InlineData("/demo/app-frame/", 390, 844)]
    public Task A_page_loads_every_resource_it_asks_for(string path, int width, int height) => RunAsync(async () =>
    {
        var misses = new List<string>();
        var open = new HashSet<IRequest>();
        Page.Request += (_, req) =>
        {
            // A stream stays open by design; it is never "loaded", so it is not waited for.
            if (req.ResourceType is not ("eventsource" or "websocket"))
            {
                lock (open)
                {
                    open.Add(req);
                }
            }
        };
        Page.RequestFinished += (_, req) => { lock (open) { open.Remove(req); } };
        Page.RequestFailed += (_, req) => { lock (open) { open.Remove(req); } };
        Page.Response += (_, res) =>
        {
            if (res.Status >= 400)
            {
                lock (misses)
                {
                    misses.Add($"{res.Status} {res.Url}");
                }
            }
        };
        Page.RequestFailed += (_, req) =>
        {
            if (!req.Url.Contains("fonts.googleapis.com", StringComparison.Ordinal)
                && !req.Url.Contains("fonts.gstatic.com", StringComparison.Ordinal))
            {
                lock (misses)
                {
                    misses.Add($"{req.Failure} {req.Url}");
                }
            }
        };
        await Page.SetViewportSizeAsync(width, height);

        await Page.GotoAsync(BaseUrl + path);
        await WaitForInteractiveAsync();
        await Page.WaitForLoadStateAsync(LoadState.Load);
        // Every request the page started has answered — not "the network fell silent", which a page that polls or keeps a
        // live connection (the app frame's work board) never does.
        var until = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            string[] waiting;
            lock (open)
            {
                waiting = [.. open.Select(r => r.Url)];
            }

            if (waiting.Length == 0)
            {
                break;
            }

            Assert.True(DateTime.UtcNow < until, $"{path} still waits on: " + string.Join(", ", waiting));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        lock (misses)
        {
            Assert.True(misses.Count == 0, $"{path} asked for what it could not load:\n  " + string.Join("\n  ", misses));
        }
    });
}
