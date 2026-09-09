#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Survey driver: shoots the site's main surfaces at desktop AND phone widths, with overflow checks.
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5050";
string tag = args.Length > 1 ? args[1] : "before";
string shotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots", tag);
Directory.CreateDirectory(shotDir);

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });

(string label, int w, int h)[] widths = [("desktop", 1280, 900), ("phone", 390, 844)];

foreach (var (wlabel, w, h) in widths)
{
    var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = w, Height = h } });

    // Landing page — direct load.
    await page.GotoAsync(baseUrl + "/", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
    await page.WaitForTimeoutAsync(1500);
    await Shoot(page, shotDir, $"home-{wlabel}", full: true);

    // Docs shell.
    await page.GotoAsync(baseUrl + "/docs", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
    try
    {
        await page.Locator(".side-nav a.side-nav-link").First
            .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
    }
    catch (Exception e) { Console.WriteLine($"  !! sidebar not visible at {wlabel}: {e.Message.Split('\n')[0]}"); }
    await page.WaitForTimeoutAsync(800);
    await Shoot(page, shotDir, $"docs-{wlabel}", full: true);

    foreach (var label in new[] { "Routing", "Todos", "Forms" })
    {
        try
        {
            await ClickSidebarAsync(page, label);
            await page.WaitForTimeoutAsync(700);
            await Shoot(page, shotDir, $"{label.ToLowerInvariant()}-{wlabel}", full: true);
        }
        catch (Exception e) { Console.WriteLine($"  !! {label} @{wlabel}: {e.Message.Split('\n')[0]}"); }
    }

    await page.CloseAsync();
}

Console.WriteLine("done.");

static async Task Shoot(IPage page, string dir, string name, bool full)
{
    await page.ScreenshotAsync(new() { Path = Path.Combine(dir, name + ".png"), FullPage = full });
    var overflow = await page.EvaluateAsync<int>(
        "() => Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth)");
    var wide = await page.EvaluateAsync<string[]>(
        @"() => Array.from(document.querySelectorAll('table,pre,.side-nav,section,div'))
              .filter(e => e.scrollWidth - e.clientWidth > 4 && getComputedStyle(e).overflowX === 'visible')
              .slice(0, 6)
              .map(e => e.tagName.toLowerCase() + '.' + (e.className || '').toString().slice(0, 40))");
    Console.WriteLine($"  {name,-24} page-overflow={(overflow > 0 ? overflow + "px OVERFLOW" : "ok")}  clipped=[{string.Join(" | ", wide)}]");
}

static async Task ClickSidebarAsync(IPage page, string label)
{
    var filter = page.Locator(".side-nav .side-nav-filter input");
    if (await filter.CountAsync() == 0 || !await filter.First.IsVisibleAsync())
    {
        // Phone: the nav may be behind a toggle.
        var toggle = page.Locator(".hamburger-btn");
        if (await toggle.CountAsync() > 0) { await toggle.First.ClickAsync(); await page.WaitForTimeoutAsync(400); }
    }
    await page.Locator(".side-nav .side-nav-filter input").First.FillAsync(label);
    var any = page.Locator($".side-nav a.side-nav-link:has-text(\"{label}\")");
    await any.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
    await page.WaitForTimeoutAsync(200);
    var example = page.Locator($".side-nav a.side-nav-link:has-text(\"{label}\"):not([href^=\"/docs/guides/\"])");
    var link = await example.CountAsync() > 0 ? example.First : any.First;
    await link.ClickAsync();
}
