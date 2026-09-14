#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Which nodes under ONE element does hydration touch, and is it still the same element afterwards?
//
// Usage (from this directory):
//   dotnet run subtreeprobe.cs http://127.0.0.1:5091 / "pre" [webkit|chromium]

using System.Text.Json;
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://127.0.0.1:5091";
string path = args.Length > 1 ? args[1] : "/";
string selector = args.Length > 2 ? args[2] : "pre";
string engine = args.Length > 3 ? args[3] : "webkit";

string instrument = """
window.__p = { log: [], booted: null, before: null };
const p = window.__p;
const SEL = __SEL__;
const desc = (x) => x.nodeType === 1 ? '<' + x.nodeName.toLowerCase() + (x.className ? '.' + String(x.className).slice(0, 40) : '') + '>' : '#' + x.nodeType + ':' + JSON.stringify(String(x.nodeValue).slice(0, 40));
document.addEventListener('DOMContentLoaded', () => {
  p.before = [...document.querySelectorAll(SEL)];
  p.beforeHtml = p.before.map(e => e.outerHTML);
});
new MutationObserver((rs) => {
  if (!p.before) return;
  for (const r of rs) {
    const hit = p.before.find(e => e === r.target || e.contains(r.target) || [...r.removedNodes].some(n => n === e || n.contains?.(e)));
    if (!hit || p.log.length > 200) continue;
    p.log.push(Math.round(performance.now()) + ' ' + r.type + ' ' + (r.attributeName ? r.attributeName + ' ' + JSON.stringify(r.oldValue) + '->' + JSON.stringify(r.target.getAttribute(r.attributeName)) : '') + ' on ' + desc(r.target) + ' +[' + [...r.addedNodes].map(desc).join(',') + '] -[' + [...r.removedNodes].map(desc).join(',') + ']' + (r.type === 'characterData' ? ' old=' + JSON.stringify(String(r.oldValue).slice(0, 60)) : ''));
  }
}).observe(document, { attributes: true, attributeOldValue: true, childList: true, subtree: true, characterData: true, characterDataOldValue: true });
p.frames = [];
const geo = () => { const e = document.querySelector(SEL); if (!e) return null; const r = e.getBoundingClientRect(); const c = e.firstElementChild; const cs = c ? getComputedStyle(c) : null; const pc = e.parentElement ? e.parentElement.getBoundingClientRect() : null;
  return [Math.round(r.width*10)/10, Math.round(r.height*10)/10, e.clientWidth, e.clientHeight, e.scrollWidth, e.offsetHeight - e.clientHeight, cs ? cs.fontFamily.slice(0,20) + '/' + cs.fontSize : '-', document.fonts ? document.fonts.status : '-', document.styleSheets.length, innerWidth - document.documentElement.clientWidth, pc ? Math.round(pc.width) : '-'].join(' '); };
const loop = () => { const g = geo(); const last = p.frames.length ? p.frames[p.frames.length-1].g : undefined; if (g !== last) p.frames.push({ t: Math.round(performance.now()), g }); if (performance.now() < 8000) requestAnimationFrame(loop); };
requestAnimationFrame(loop);
const prev = window.raskAfterMorph;
window.raskAfterMorph = function () { if (p.booted === null) p.booted = Math.round(performance.now()); if (typeof prev === 'function') prev.apply(this, arguments); };
""".Replace("__SEL__", ("\"" + selector.Replace("\"", "\\\"") + "\""));

using var pw = await Playwright.CreateAsync();
await using var browser = await (engine == "webkit" ? pw.Webkit : pw.Chromium).LaunchAsync(new() { Headless = true });
await using var ctx = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
var page = await ctx.NewPageAsync();
// A cold visit, emulated: hold every font response back this long, so a font-display: swap face arrives
// AFTER first paint the way it does over a real network rather than in the 5ms a loopback server takes.
int fontDelay = args.Length > 4 ? int.Parse(args[4]) : 0;
if (fontDelay > 0)
{
    await page.RouteAsync("**/fonts/**", async route =>
    {
        await Task.Delay(fontDelay);
        await route.ContinueAsync();
    });
}

await page.AddInitScriptAsync(instrument);
await page.GotoAsync(baseUrl + path, new() { Timeout = 120_000 });
await page.WaitForTimeoutAsync(8000);
var r = await page.EvaluateAsync<JsonElement>("""
() => {
  const p = window.__p, now = [...document.querySelectorAll(__SEL__)];
  return {
    booted: p.booted,
    count: now.length,
    same: p.before ? p.before.map((e, i) => e === now[i] && e.isConnected) : [],
    htmlSame: p.beforeHtml ? p.beforeHtml.map((h, i) => now[i] && h === now[i].outerHTML) : [],
    diff: p.beforeHtml ? p.beforeHtml.map((h, i) => { const a = h, b = now[i] ? now[i].outerHTML : ''; let k = 0; while (k < a.length && a[k] === b[k]) k++; return k === a.length && a.length === b.length ? '' : 'at ' + k + ': before=' + JSON.stringify(a.slice(k, k + 80)) + ' after=' + JSON.stringify(b.slice(k, k + 80)); }) : [],
    log: p.log,
    frames: p.frames.map(f => f.t + 'ms ' + f.g),
  };
}
""".Replace("__SEL__", ("\"" + selector.Replace("\"", "\\\"") + "\"")));
Console.WriteLine($"=== {engine} {baseUrl}{path} '{selector}' booted={r.GetProperty("booted")} count={r.GetProperty("count")} ===");
Console.WriteLine("same element:  " + r.GetProperty("same"));
Console.WriteLine("same outerHTML: " + r.GetProperty("htmlSame"));
foreach (var d in r.GetProperty("diff").EnumerateArray())
{
    if (d.GetString() is { Length: > 0 } s)
    {
        Console.WriteLine("  " + s);
    }
}

Console.WriteLine("frames: w h clientW clientH scrollW hScrollbarH font fonts sheets vScrollbarW parentW");
foreach (var l in r.GetProperty("frames").EnumerateArray())
{
    Console.WriteLine("  " + l.GetString());
}

foreach (var l in r.GetProperty("log").EnumerateArray())
{
    Console.WriteLine("  " + l.GetString());
}
