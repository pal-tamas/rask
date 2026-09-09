#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Probe: does a chosen theme survive the trip from the landing page to /docs, and does the bar say
// which theme is on?
using Microsoft.Playwright;

var baseUrl = args.Length > 0 ? args[0] : "http://127.0.0.1:5050";
var pick = args.Length > 1 ? args[1] : "synthwave";

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });

async Task<string> StateAsync(string where)
{
    var theme = await page.EvaluateAsync<string>("() => document.documentElement.getAttribute('data-theme')");
    var label = await page.EvaluateAsync<string>(
        "() => { const e = document.querySelector('.ui-theme-current'); return e ? e.textContent.trim() : '(none)'; }");
    var check = await page.EvaluateAsync<string>(
        "() => { const r = document.querySelector('input.theme-controller:checked'); return r ? r.value : '(none checked)'; }");
    var bg = await page.EvaluateAsync<string>(
        "() => getComputedStyle(document.body).backgroundColor");
    Console.WriteLine($"  {where,-28} data-theme={theme,-12} label={label,-12} checked={check,-14} bodyBg={bg}");
    return theme ?? "";
}

await page.GotoAsync(baseUrl + "/", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
await page.WaitForTimeoutAsync(1500);
await StateAsync("landing, before pick");

// Pick a theme the way a reader does: check the radio in the dropdown.
await page.EvaluateAsync(
    @"t => { const r = [...document.querySelectorAll('input.theme-controller')].find(x => x.value === t);
             if (!r) throw new Error('no radio for ' + t);
             r.checked = true; r.dispatchEvent(new Event('change', { bubbles: true })); }",
    pick);
await page.WaitForTimeoutAsync(400);
await StateAsync($"landing, picked {pick}");

// Navigate to the docs through the app's own link, so this is the SPA route change, not a reload.
await page.Locator("a[href='/docs/guides'], a[href='/docs']").First.ClickAsync();
await page.WaitForTimeoutAsync(2000);
var afterNav = await StateAsync("after navigating to /docs");

// And a hard reload, which is the other way a reader loses a preference.
await page.ReloadAsync(new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
await page.WaitForTimeoutAsync(2000);
var afterReload = await StateAsync("after full reload");

Console.WriteLine();
Console.WriteLine($"survives navigation: {(afterNav == pick ? "YES" : "NO  (got " + afterNav + ")")}");
Console.WriteLine($"survives reload:     {(afterReload == pick ? "YES" : "NO  (got " + afterReload + ")")}");
await page.ScreenshotAsync(new() { Path = "screenshots/theme-persisted.png" });
