#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// WHAT changes size when the runtime takes a prerendered page over?
//
// Snapshots every element's box (keyed by its DOM path) as soon as the prerendered document is parsed and the
// fonts are ready, again after the runtime's first frame, and prints the DEEPEST elements whose height or top
// changed — the ones whose own content differs, not every ancestor that grew with them. Also prints the elements
// that exist on only one side. A prerendered page that hydrates cleanly prints nothing.
//
// Usage (from this directory):
//   dotnet run growprobe.cs -- http://127.0.0.1:5090 /docs/guides/one-person-framework/ [chromium|webkit] [width]

using System.Text.Json;
using Microsoft.Playwright;

string baseUrl = args[0].TrimEnd('/');
string path = args[1];
string engine = args.Length > 2 ? args[2] : "chromium";
int width = args.Length > 3 ? int.Parse(args[3]) : 1280;

const string Instrument = """
window.__g = { before: null, after: null, booted: null };
const g = window.__g;
const snap = () => {
  const out = {};
  const walk = (el, path) => {
    const r = el.getBoundingClientRect();
    const own = [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.nodeValue).join('').trim().slice(0, 50);
    out[path] = { h: Math.round(r.height), t: Math.round(r.top + scrollY), w: Math.round(r.width), text: own,
                  tag: el.nodeName.toLowerCase() + (el.id ? '#' + el.id : '') + (typeof el.className === 'string' && el.className ? '.' + el.className.trim().split(/\s+/).slice(0, 3).join('.') : ''),
                  kids: el.children.length, len: (el.textContent || '').length };
    const counts = {};
    for (const c of el.children) {
      const k = c.nodeName.toLowerCase();
      counts[k] = (counts[k] || 0) + 1;
      walk(c, path + '>' + k + ':' + counts[k]);
    }
  };
  walk(document.body, 'body');
  return out;
};
document.addEventListener('DOMContentLoaded', () => { document.fonts.ready.then(() => { if (!g.before && document.documentElement.hasAttribute('data-rask-prerendered')) g.before = snap(); }); });
const prev = window.raskAfterMorph;
window.raskAfterMorph = function () {
  if (typeof prev === 'function') prev.apply(this, arguments);
  if (g.booted === null) { g.booted = Math.round(performance.now()); setTimeout(() => { g.after = snap(); }, 1500); }
};
""";

using var pw = await Playwright.CreateAsync();
await using var browser = await (engine == "webkit" ? pw.Webkit : pw.Chromium).LaunchAsync(new() { Headless = true });
await using var ctx = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
var page = await ctx.NewPageAsync();
await page.AddInitScriptAsync(Instrument);
await page.GotoAsync(baseUrl + path, new() { Timeout = 120_000 });
await page.WaitForFunctionAsync("() => window.__g.after !== null", null, new() { Timeout = 60_000 });
using var doc = JsonDocument.Parse(await page.EvaluateAsync<string>("() => JSON.stringify(window.__g)"));
var g = doc.RootElement;
if (g.GetProperty("before").ValueKind != JsonValueKind.Object)
{
    Console.WriteLine("NO PRERENDERED SNAPSHOT — the runtime took over before the fonts were ready; re-run.");
    return;
}

var before = g.GetProperty("before").EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
var after = g.GetProperty("after").EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
Console.WriteLine($"=== {engine} {width}px {baseUrl}{path} booted={g.GetProperty("booted")} body h {before["body"].GetProperty("h")} -> {after["body"].GetProperty("h")} ===");

string Desc(JsonElement e) => $"{e.GetProperty("tag")} \"{e.GetProperty("text")}\" h={e.GetProperty("h")} top={e.GetProperty("t")} kids={e.GetProperty("kids")} textLen={e.GetProperty("len")}";

var changed = before.Keys.Intersect(after.Keys)
    .Where(k => before[k].GetProperty("h").GetInt32() != after[k].GetProperty("h").GetInt32()
                || before[k].GetProperty("kids").GetInt32() != after[k].GetProperty("kids").GetInt32()
                || before[k].GetProperty("len").GetInt32() != after[k].GetProperty("len").GetInt32())
    .ToList();
// Deepest only: drop a changed element when one of its descendants also changed.
var deepest = changed.Where(k => !changed.Any(o => o != k && o.StartsWith(k + ">", StringComparison.Ordinal))).ToList();
Console.WriteLine($"changed (deepest {deepest.Count} of {changed.Count}):");
foreach (var k in deepest.Take(25))
{
    Console.WriteLine($"  {k}\n    before {Desc(before[k])}\n    after  {Desc(after[k])}");
}

var onlyAfter = after.Keys.Except(before.Keys).Where(k => !after.Keys.Except(before.Keys).Any(o => o != k && k.StartsWith(o + ">", StringComparison.Ordinal))).ToList();
var onlyBefore = before.Keys.Except(after.Keys).Where(k => !before.Keys.Except(after.Keys).Any(o => o != k && k.StartsWith(o + ">", StringComparison.Ordinal))).ToList();
Console.WriteLine($"only after hydration ({onlyAfter.Count}):");
foreach (var k in onlyAfter.Take(15))
{
    Console.WriteLine($"  {k}  {Desc(after[k])}");
}

Console.WriteLine($"only in the prerender ({onlyBefore.Count}):");
foreach (var k in onlyBefore.Take(15))
{
    Console.WriteLine($"  {k}  {Desc(before[k])}");
}
