#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// What happens to the scroll position when a reader presses REFRESH part-way down a page?
//
// Scrolls to a depth, reloads, and records scrollY in every requestAnimationFrame from before the first
// byte of page script until well after the runtime has taken over — plus every programmatic scroll
// (window.scrollTo / scroll / scrollBy, Element.scrollIntoView, scrollTop writes on the root) with the
// stack that made it. A row per CHANGE, so a jump to the top and back is two rows, not a missed frame.
//
// Usage (from this directory):
//   dotnet run reloadprobe.cs -- http://127.0.0.1:5090 / 2400 [webkit|chromium]

using System.Text.Json;
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://127.0.0.1:5090";
string path = args.Length > 1 ? args[1] : "/";
int depth = args.Length > 2 ? int.Parse(args[2]) : 2400;
string engine = args.Length > 3 ? args[3] : "chromium";

const string Instrument = """
window.__s = { rows: [], calls: [], booted: null, restoration: null };
const s = window.__s;
const now = () => Math.round(performance.now());
const note = (what) => { if (s.calls.length < 40) s.calls.push(now() + 'ms ' + what + ' at y=' + Math.round(scrollY) + '\n      ' + (new Error().stack || '').split('\n').slice(2, 5).map(l => l.trim()).join('\n      ')); };
for (const name of ['scrollTo', 'scroll', 'scrollBy']) {
  const orig = window[name];
  window[name] = function () { note('window.' + name + '(' + [...arguments].map(a => JSON.stringify(a)).join(', ') + ')'); return orig.apply(this, arguments); };
}
const siv = Element.prototype.scrollIntoView;
Element.prototype.scrollIntoView = function () { note('<' + this.nodeName.toLowerCase() + (this.id ? '#' + this.id : '') + '>.scrollIntoView'); return siv.apply(this, arguments); };
let last = null;
const loop = () => {
  const y = Math.round(scrollY), h = document.documentElement ? document.documentElement.scrollHeight : 0;
  const key = y + '|' + h;
  if (key !== last) { last = key; s.rows.push(now() + 'ms y=' + y + ' docH=' + h + ' prerendered=' + !!(document.documentElement && document.documentElement.hasAttribute('data-rask-prerendered')) + ' ready=' + document.readyState); }
  if (performance.now() < 9000) requestAnimationFrame(loop);
};
requestAnimationFrame(loop);
addEventListener('scroll', () => { if (s.rows.length < 200) s.rows.push(now() + 'ms   (scroll event) y=' + Math.round(scrollY)); }, { passive: true });
document.addEventListener('DOMContentLoaded', () => { s.restoration = history.scrollRestoration; });
const prev = window.raskAfterMorph;
window.raskAfterMorph = function () { if (s.booted === null) s.booted = now(); if (typeof prev === 'function') prev.apply(this, arguments); };
""";

using var pw = await Playwright.CreateAsync();
await using var browser = await (engine == "webkit" ? pw.Webkit : pw.Chromium).LaunchAsync(new() { Headless = true });
await using var ctx = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
var page = await ctx.NewPageAsync();
await page.AddInitScriptAsync(Instrument);

await page.GotoAsync(baseUrl + path, new() { Timeout = 120_000 });
await page.WaitForFunctionAsync("() => window.__s && window.__s.booted !== null", null, new() { Timeout = 60_000 });
await page.EvaluateAsync($"() => window.scrollTo(0, {depth})");
await page.WaitForTimeoutAsync(800);
var before = await page.EvaluateAsync<int>("() => Math.round(scrollY)");

await page.ReloadAsync(new() { Timeout = 120_000 });
await page.WaitForTimeoutAsync(9000);
var s = await page.EvaluateAsync<JsonElement>("() => window.__s");

Console.WriteLine($"=== {engine} {baseUrl}{path} scrolled to {before}, reloaded; booted={s.GetProperty("booted")} scrollRestoration={s.GetProperty("restoration")} ===");
foreach (var row in s.GetProperty("rows").EnumerateArray())
{
    Console.WriteLine("  " + row.GetString());
}

Console.WriteLine("programmatic scrolls:");
foreach (var call in s.GetProperty("calls").EnumerateArray())
{
    Console.WriteLine("  " + call.GetString());
}
