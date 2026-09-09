#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Driver for the Rask site (site/Rask.Site) — the app published to rask.sh.
//
// One browser-WASM app: the landing page at /, the showcase and guides at /docs. `dotnet run` serves
// it through the SDK's WasmAppHost, the browser downloads dotnet.wasm + assemblies, boots the Mono
// runtime, and renders + handles events locally via JSImport/JSExport — there is NO server WebSocket.
// `curl` sees only the shell HTML (and the framework asset paths are fingerprinted + resolved through
// the page's import map, so `/_framework/dotnet.js` even 404s on a direct GET). Only a real browser
// boots it, which is the whole reason this file exists.
//
// It reuses the repo's existing Microsoft.Playwright dependency + already-installed browsers, and is
// a .NET 10 file-based app — no Node, no csproj. Run with `dotnet run driver.cs`.
//
// The readiness signal is a mounted first render, not a network event: `WaitUntilState.NetworkIdle`
// resolves while the page is still the boot shell. We wait for the sidebar nav — the same signal the
// E2E suite uses — with a generous timeout, because a cold WASM boot downloads the whole runtime.
//
// WasmAppHost installs no SPA fallback, so a DEEP LINK 404s: /docs/todos is not a file on disk. Every
// path below is reached the way the E2E suite reaches it — load the shell, then navigate in-app.
//
// Prereq: the app must already be running (see SKILL.md "Run").
//
// Usage (run from THIS directory so screenshots land in ./screenshots/):
//   dotnet run driver.cs                       # shots (default)
//   dotnet run driver.cs todos                 # interactive: toggle a todo, assert it flips CLIENT-SIDE
//   dotnet run driver.cs all
//   dotnet run driver.cs all http://localhost:5051   # override base URL

using Microsoft.Playwright;

string cmd = args.Length > 0 ? args[0] : "shots";
string baseUrl = args.Length > 1 ? args[1] : "http://localhost:5050";
string shotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
Directory.CreateDirectory(shotDir);

// The landing page is a direct load; everything under /docs is reached through the sidebar, because
// there is no SPA fallback to answer a deep link. `null` means "already there — just shoot it".
(string? sidebar, string name)[] pages =
[
    (null, "home"),
    ("Todos", "todos"),
    ("Routing", "routing"),
];

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });

Console.WriteLine($"Driving {baseUrl}  ({cmd})  [WASM — client-side]");

if (cmd is "shots" or "all")
{
    // The landing page first, on its own load: it is the front door and the only route a static host
    // answers directly.
    var res = await page.GotoAsync(baseUrl + "/", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
    await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, "home.png") });
    Console.WriteLine($"  OK /          {res!.Status}  \"{await page.TitleAsync()}\"  -> home.png");

    await GotoShowcaseAsync(page, baseUrl);
    foreach (var (sidebar, name) in pages)
    {
        if (sidebar is null)
        {
            continue;
        }

        await ClickSidebarAsync(page, sidebar);
        await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, name + ".png") });
        Console.WriteLine($"  OK {sidebar,-10} {page.Url}  \"{await page.TitleAsync()}\"  -> {name}.png");
    }
}

if (cmd is "todos" or "all")
{
    await GotoShowcaseAsync(page, baseUrl);
    await ClickSidebarAsync(page, "Todos");

    var box = page.Locator("#todo-list li").First.Locator("input[type='checkbox']");
    await box.First.WaitForAsync(new() { Timeout = 30000 });
    var before = await box.First.IsCheckedAsync();
    await box.First.ClickAsync();
    // No WS round-trip — the WASM runtime handles the event and re-renders locally. Wait for the flip.
    await page.WaitForFunctionAsync(
        "was => { const el = document.querySelector(\"#todo-list li input[type='checkbox']\"); return el && el.checked !== was; }",
        before, new() { Timeout = 10000 });
    var after = await box.First.IsCheckedAsync();
    await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, "todos-toggled.png") });
    if (after == before)
    {
        throw new Exception($"toggle did NOT flip client-side (still {after})");
    }

    Console.WriteLine($"  OK /docs/todos checkbox toggled CLIENT-SIDE: {before} -> {after}  -> todos-toggled.png");
}

Console.WriteLine("done.");

// Load the showcase shell and wait for the WASM runtime to mount its first render.
static async Task GotoShowcaseAsync(IPage page, string baseUrl)
{
    await page.GotoAsync(baseUrl + "/docs", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
    await page.Locator(".side-nav a.side-nav-link").First
        .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
}

// Sidebar groups are collapsed by default and a collapsed link is display:none, so Playwright's text
// engines cannot even see it. Navigate the way a user does with a long list: type the label into the
// filter, which narrows the sidebar to the matching link. A label can name both a guide and a demo
// (Routing, Lifecycle), and Guides render first — so prefer the link that is not a guide.
static async Task ClickSidebarAsync(IPage page, string label)
{
    await page.Locator(".side-nav .side-nav-filter input").FillAsync(label);
    var any = page.Locator($".side-nav a.side-nav-link:has-text(\"{label}\")");
    await any.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
    await page.WaitForTimeoutAsync(200);
    var example = page.Locator(
        $".side-nav a.side-nav-link:has-text(\"{label}\"):not([href^=\"/docs/guides/\"])");
    var link = await example.CountAsync() > 0 ? example.First : any.First;
    await link.ClickAsync();
}
