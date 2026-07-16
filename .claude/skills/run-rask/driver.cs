#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Driver for the Rask Server showcase (samples/Rask.Example.Server).
//
// The showcase is a server-rendered app whose components live-update over a WebSocket: the browser
// sends `data-rask-on-*` events, the server re-renders and streams back a DOM diff. `curl` sees only
// the first server-rendered HTML — it can NEVER observe the live loop. This driver uses the repo's
// existing Microsoft.Playwright dependency (same version the E2E suite uses; browsers already in the
// ms-playwright cache) to load pages, screenshot them, and exercise the WS round-trip.
//
// It's a .NET 10 *file-based app* — no csproj, no Node, no npm. Run it with `dotnet run driver.cs`.
// The reflection-enabled property is required: file-based apps disable reflection-based System.Text.
// Json by default, which Playwright's transport needs.
//
// Prereq: the server must already be running (see SKILL.md "Run" — launch it, then run this).
//
// Usage (run from THIS directory so screenshots land in ./screenshots/):
//   dotnet run driver.cs                       # shots (default): screenshot home + a few pages
//   dotnet run driver.cs todos                 # interactive: toggle a todo checkbox, assert the diff
//   dotnet run driver.cs all                   # shots + todos
//   dotnet run driver.cs all http://localhost:5199   # override the base URL

using Microsoft.Playwright;

string cmd = args.Length > 0 ? args[0] : "shots";
string baseUrl = args.Length > 1 ? args[1] : "http://localhost:5099";
string shotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
Directory.CreateDirectory(shotDir);

// Home plus a spread of the showcase's pages.
(string path, string name)[] pages =
[
    ("/", "home"),
    ("/todos", "todos"),
    ("/table", "table"),
    ("/routing-demo/about", "routing-about"),
];

using var pw = await Playwright.CreateAsync();
// Headless Chromium from the already-installed ms-playwright cache — no download.
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });

Console.WriteLine($"Driving {baseUrl}  ({cmd})");

if (cmd is "shots" or "all")
{
    foreach (var (path, name) in pages)
    {
        var res = await page.GotoAsync(baseUrl + path, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 20000 });
        await WaitLiveAsync(page);
        var file = Path.Combine(shotDir, name + ".png");
        await page.ScreenshotAsync(new() { Path = file });
        Console.WriteLine($"  OK {path,-22} {res!.Status}  \"{await page.TitleAsync()}\"  -> {name}.png");
    }
}

if (cmd is "todos" or "all")
{
    await page.GotoAsync(baseUrl + "/todos", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 20000 });
    await WaitLiveAsync(page);
    var boxes = page.Locator("input.form-check-input[type=checkbox]");
    if (await boxes.CountAsync() == 0)
        throw new Exception("no todo checkboxes found — page shape changed?");

    var before = await boxes.First.IsCheckedAsync();
    await boxes.First.ClickAsync();
    // The re-render arrives over the WS; wait for the checkbox to reflect the flipped state.
    await page.WaitForFunctionAsync(
        "was => { const el = document.querySelector('input.form-check-input[type=checkbox]'); return el && el.checked !== was; }",
        before, new() { Timeout = 10000 });
    var after = await page.Locator("input.form-check-input[type=checkbox]").First.IsCheckedAsync();
    await page.ScreenshotAsync(new() { Path = Path.Combine(shotDir, "todos-toggled.png") });
    if (after == before)
        throw new Exception($"toggle did NOT round-trip (still {after})");
    Console.WriteLine($"  OK /todos checkbox toggled over WS: {before} -> {after}  -> todos-toggled.png");
}

Console.WriteLine("done.");

// The live client swaps `data-rask-connecting` off <html> once the WebSocket is open. Waiting for it
// guarantees the interactive loop is live before we click anything (already gone? fine — we move on).
static async Task WaitLiveAsync(IPage page)
{
    try
    {
        await page.WaitForFunctionAsync(
            "() => !document.documentElement.hasAttribute('data-rask-connecting')",
            null, new() { Timeout = 15000 });
    }
    catch (TimeoutException) { }
}
