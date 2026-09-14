#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Every PAINTED frame of a page load, not a 50ms sample of it.
//
// A flicker a reader sees is usually one or two frames (16-33ms), which a 50ms timer can step straight
// over. This records a sample in requestAnimationFrame — after style recalc, before paint — so each row
// is what the next paint shows. It also counts the DOM mutations that landed in each frame, split by
// head/body, so a visual change can be tied to the morph that caused it.
//
// Usage (from this directory):
//   dotnet run frameprobe.cs https://rask.sh [/path] [cpuThrottle]

using System.Text.Json;
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://localhost:5090";
string path = args.Length > 1 ? args[1] : "/";
int throttle = args.Length > 2 ? int.Parse(args[2]) : 1;

const string Instrument = """
window.__f = { frames: [], headMut: 0, bodyMut: 0, htmlMut: 0, booted: null, log: [] };
const f = window.__f;
const q = (s) => document.querySelector(s);
const snap = () => {
  const b = document.body;
  if (!b) { return null; }
  const cs = getComputedStyle(b);
  const h1 = q('h1');
  const main = q('main') || b.firstElementChild;
  const r = main ? main.getBoundingClientRect() : null;
  const h1cs = h1 ? getComputedStyle(h1) : null;
  const imgs = [...document.images].filter(i => i.getBoundingClientRect().top < innerHeight);
  return {
    t: Math.round(performance.now()),
    sheets: document.styleSheets.length,
    bg: cs.backgroundColor, fg: cs.color, fam: cs.fontFamily.slice(0, 16),
    theme: document.documentElement.getAttribute('data-theme') || '-',
    cls: (document.documentElement.className || '-').slice(0, 30),
    kids: b.childElementCount, h: document.documentElement.scrollHeight,
    mainTop: r ? Math.round(r.top) : null, mainH: r ? Math.round(r.height) : null,
    h1: h1 ? (h1cs.fontSize + '/' + h1cs.fontWeight + '/' + h1cs.fontFamily.slice(0, 12) + '/' + h1cs.opacity + '/' + Math.round(h1.getBoundingClientRect().top)) : '-',
    vis: imgs.filter(i => !i.complete).length,
    fonts: document.fonts ? document.fonts.status : '-',
    hm: f.headMut, bm: f.bodyMut, xm: f.htmlMut, sy: Math.round(scrollY),
  };
};
const loop = () => { try { const s = snap(); if (s) { f.frames.push(s); } } catch (e) { if (f.log.length < 80) f.log.push('snap error: ' + e); } if (performance.now() < 12000) { requestAnimationFrame(loop); } };
requestAnimationFrame(loop);
new MutationObserver((rs) => {
  for (const r of rs) {
    let n = r.target;
    if (r.type === 'attributes' && performance.now() > 400 && f.log.length < 120) f.log.push(Math.round(performance.now()) + ' ATTR ' + r.attributeName + ' ' + JSON.stringify(r.oldValue) + ' -> ' + JSON.stringify(n.getAttribute(r.attributeName)) + ' on <' + n.nodeName + ' ' + [...n.attributes].map(a => a.name + '=' + a.value.slice(0, 40)).join(' ') + '>');
    if (n === document.documentElement || n === document) { f.htmlMut++; }
    else if (document.head && document.head.contains(n)) { f.headMut++; if (performance.now() > 150 && f.log.length < 80) f.log.push(Math.round(performance.now()) + ' head ' + r.type + ' ' + (r.attributeName || '') + ' +' + r.addedNodes.length + ' -' + r.removedNodes.length + ' ' + [...r.addedNodes, ...r.removedNodes].map(x => x.outerHTML ? x.outerHTML.slice(0, 90) : '#t').join(' | ')); }
    else { f.bodyMut++; if (performance.now() > 400 && f.log.length < 120) f.log.push(Math.round(performance.now()) + ' body ' + r.type + ' ' + (r.attributeName || '') + ' on ' + (n.outerHTML ? n.outerHTML.slice(0, 80) : '#t') + ' +' + [...r.addedNodes].map(x => x.outerHTML ? x.outerHTML.slice(0, 120) : '#t:' + String(x.textContent).slice(0, 30)).join(' | ') + ' -' + [...r.removedNodes].map(x => x.outerHTML ? x.outerHTML.slice(0, 120) : '#t:' + String(x.textContent).slice(0, 30)).join(' | ')); }
  }
}).observe(document, { attributes: true, attributeOldValue: true, childList: true, subtree: true, characterData: true });
const prev = window.raskAfterMorph;
window.raskAfterMorph = function () { if (f.booted === null) f.booted = Math.round(performance.now()); if (typeof prev === 'function') prev.apply(this, arguments); };
""";

using var pw = await Playwright.CreateAsync();
var engine = args.Length > 3 ? args[3] : "chromium";
await using var browser = await (engine == "webkit" ? pw.Webkit : pw.Chromium).LaunchAsync(new() { Headless = true });
await using var ctx = await browser.NewContextAsync(new()
{
    ViewportSize = new() { Width = 1280, Height = 900 },
    RecordVideoDir = engine == "webkit" ? Path.Combine("screenshots", "video") : null,
    // Playwright refuses a size without a directory, so the size rides along only when recording.
    RecordVideoSize = engine == "webkit" ? new() { Width = 1280, Height = 900 } : null,
});
var page = await ctx.NewPageAsync();
if (throttle > 1 && engine != "webkit")
{
    var cdp = await ctx.NewCDPSessionAsync(page);
    await cdp.SendAsync("Emulation.setCPUThrottlingRate", new() { ["rate"] = throttle });
}

await page.AddInitScriptAsync(Instrument);
await page.GotoAsync(baseUrl + path, new() { Timeout = 120_000 });
await page.WaitForTimeoutAsync(9000);
var f = await page.EvaluateAsync<JsonElement>("() => window.__f");

Console.WriteLine($"=== {baseUrl}{path} (cpu x{throttle}) booted={f.GetProperty("booted")} ===");
string? last = null;
foreach (var s in f.GetProperty("frames").EnumerateArray())
{
    var key = string.Join('|', new[] { "sheets", "bg", "fg", "fam", "theme", "cls", "kids", "h", "mainTop", "mainH", "h1", "vis", "fonts", "sy" }
        .Select(k => s.GetProperty(k).ToString()));
    if (key == last)
    {
        continue;
    }

    last = key;
    Console.WriteLine($"{s.GetProperty("t"),6}ms sheets={s.GetProperty("sheets"),-2} bg={s.GetProperty("bg"),-22} fg={s.GetProperty("fg"),-22} fam={s.GetProperty("fam"),-16} theme={s.GetProperty("theme")} cls={s.GetProperty("cls")} kids={s.GetProperty("kids")} h={s.GetProperty("h")} main={s.GetProperty("mainTop")}/{s.GetProperty("mainH")} h1={s.GetProperty("h1")} imgPending={s.GetProperty("vis")} fonts={s.GetProperty("fonts")} sy={s.GetProperty("sy")} mut(h/b/x)={s.GetProperty("hm")}/{s.GetProperty("bm")}/{s.GetProperty("xm")}");
}

foreach (var l in f.GetProperty("log").EnumerateArray())
{
    Console.WriteLine("  " + l.GetString());
}
