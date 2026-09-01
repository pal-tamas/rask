#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Screenshots the built-in operator console (Rask.Dashboard) out of samples/Rask.Example.Shop, which is
// the sample that actually registers it. Signs in first: the Shop defines the access policy, so /_rask
// redirects to /login for anyone else.
//
// Shoots every page twice — 1280px and 390px — because the console is built mobile-first and the two
// layouts are genuinely different markup paths (columns drop, the leader rules disappear, the sheet
// becomes a bottom sheet). One width proves nothing about the other.
//
// Usage (run from THIS directory so screenshots land in ./screenshots/):
//   dotnet run dashboard-driver.cs [baseUrl]

using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5123";
string shotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
Directory.CreateDirectory(shotDir);

(string path, string name)[] pages =
[
    ("/_rask", "ops-overview"),
    ("/_rask/queues/jobs", "ops-queue"),
    ("/_rask/cache", "ops-cache"),
    ("/_rask/logs?view=history", "ops-logs"),
    ("/_rask/system", "ops-system"),
];

(int w, int h, string tag)[] sizes = [(1280, 900, "desktop"), (390, 844, "mobile")];

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });

foreach (var (w, h, tag) in sizes)
{
    await using var ctx = await browser.NewContextAsync(new()
    {
        BaseURL = baseUrl,
        ViewportSize = new() { Width = w, Height = h },
    });
    var page = await ctx.NewPageAsync();

    // Sign in. Waiting for the Sign-out button rather than the URL: on the Server host the cookie is
    // committed by a client navigation the live session asks for, so the URL leaves /login before the
    // cookie exists and the next Goto would race it straight back to /login.
    await page.GotoAsync("/login");
    await page.FillAsync("#username", "alice");
    await page.FillAsync("#password", "password");
    await page.ClickAsync("button[type=submit]");
    await page.Locator("button:has-text('Sign out')").WaitForAsync(new() { Timeout = 20000 });

    Console.WriteLine($"── {tag} ({w}x{h}) ────────────────────────────");

    foreach (var (path, name) in pages)
    {
        var res = await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 25000 });

        // The panels load on PollingPanel's async mount, so the first paint is the spinner.
        await page.Locator("nav a:has-text('Overview')").First.WaitForAsync(new() { Timeout = 15000 });
        await page.WaitForTimeoutAsync(900);

        var file = Path.Combine(shotDir, $"{name}-{tag}.png");
        await page.ScreenshotAsync(new() { Path = file, FullPage = true });

        // The one failure that makes a phone layout unusable rather than merely ugly.
        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");

        // And the tables separately: they carry their own overflow-x as a backstop, so a column that
        // failed to collapse hides its content behind an internal scrollbar while the document check
        // above stays green.
        var wideTables = await page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('table')]"
            + ".filter(t => t.scrollWidth > t.parentElement.clientWidth + 1).length");

        var flag = overflow ? "PAGE-OVERFLOW" : wideTables > 0 ? $"TABLE-WIDE({wideTables})" : "ok";
        Console.WriteLine($"  {flag,-15} {path,-28} {res!.Status} -> {name}-{tag}.png");
    }
}

Console.WriteLine($"\nScreenshots in {shotDir}");
