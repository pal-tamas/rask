#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Driver for the Rask WASM showcase (samples/Rask.Example.Wasm, served by Rask.Example.Wasm.Host).
//
// Same components as the Server showcase, but running CLIENT-SIDE on WebAssembly: the host serves a
// published AppBundle, the browser downloads dotnet.wasm + assemblies, boots the Mono runtime, and
// renders + handles events locally via JSImport/JSExport — there is NO server WebSocket. `curl` sees
// only the shell HTML (and the framework asset paths are fingerprinted + resolved through the page's
// import map, so `/_framework/dotnet.js` even 404s on a direct GET). Only a real browser boots it.
//
// This driver reuses the repo's existing Microsoft.Playwright dependency + already-installed browsers.
// It's a .NET 10 file-based app — no Node, no csproj. Run with `dotnet run driver.cs`.
//
// The readiness signal differs from the Server host: there's no `data-rask-connecting` WS attribute.
// The app is "up" once the WASM runtime has booted and mounted the first render — we wait for the
// sidebar nav to appear (same signal the E2E WasmExampleTests uses, with a generous timeout because
// the cold WASM boot is slow).
//
// Prereq: the host must already be running (see SKILL.md "Run").
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

(string path, string name)[] pages =
[
    ("/", "home"),
    ("/todos", "todos"),
    ("/table", "table"),
];

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });

Console.WriteLine($"Driving {baseUrl}  ({cmd})  [WASM — client-side]");

if (cmd is "shots" or "all")
{
    foreach (var (path, name) in pages)
    {
        var res = await page.GotoAsync(baseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
        await WaitBootedAsync(page);
        var file = Path.Combine(shotDir, name + ".png");
        await page.ScreenshotAsync(new() { Path = file });
        Console.WriteLine($"  OK {path,-10} {res!.Status}  \"{await page.TitleAsync()}\"  -> {name}.png");
    }
}

if (cmd is "todos" or "all")
{
    await page.GotoAsync(baseUrl + "/todos", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });
    await WaitBootedAsync(page);
    var boxes = page.Locator("input.form-check-input[type=checkbox]");
    // The list is client-rendered after boot — wait for at least one checkbox to exist.
    await boxes.First.WaitForAsync(new() { Timeout = 30000 });
    var before = await boxes.First.IsCheckedAsync();
    await boxes.First.ClickAsync();
    // No WS round-trip — the WASM runtime handles the event and re-renders locally. Wait for the flip.
    await page.WaitForFunctionAsync(
        "was => { const el = document.querySelector('input.form-check-input[type=checkbox]'); return el && el.checked !== was; }",
        before, new() { Timeout = 10000 });
    var after = await page.Locator("input.form-check-input[type=checkbox]").First.IsCheckedAsync();
    await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, "todos-toggled.png") });
    if (after == before)
        throw new Exception($"toggle did NOT flip client-side (still {after})");
    Console.WriteLine($"  OK /todos checkbox toggled CLIENT-SIDE: {before} -> {after}  -> todos-toggled.png");
}

Console.WriteLine("done.");

// WASM readiness: the runtime boots and mounts the first render, then the sidebar nav appears.
// Cold boot downloads the runtime + assemblies, so this can take tens of seconds.
static async Task WaitBootedAsync(IPage page)
{
    await page.Locator(".side-nav a.side-nav-link").First
        .WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 60000 });
}
