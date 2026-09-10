#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Lists the sidebar links the showcase actually renders, with their hrefs.
//
// Exists because a driver that guesses a nav label fails with a 30s Playwright timeout and no clue which
// half was wrong — the label, or the fact that the row is behind a filter or a collapsed group.
//
//   dotnet run navlist.cs http://127.0.0.1:PORT

using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5050";

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();

await page.GotoAsync(baseUrl + "/docs", new() { Timeout = 120_000 });
await page.WaitForSelectorAsync(".side-nav a.side-nav-link", new() { Timeout = 120_000 });

var rows = await page.EvaluateAsync<string[]>("""
    () => Array.from(document.querySelectorAll('.side-nav a.side-nav-link'))
        .map(a => (a.textContent || '').trim().replace(/\s+/g, ' ') + '   ->   ' + a.getAttribute('href'))
    """);

Console.WriteLine($"{rows.Length} sidebar link(s):");
foreach (var row in rows)
{
    Console.WriteLine("  " + row);
}
