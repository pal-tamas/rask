#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// What happens between the prerendered paint and the hydrated one.
//
// The published bundle is NOT a boot shell: `dotnet publish` prerenders each route to real HTML, so the
// first paint is the finished page and the WASM runtime then mounts into it. Any flicker a reader
// reports on rask.sh lives in that handover, and it is invisible to the obvious instrument: a probe that
// waits for an <h1> is satisfied by the PRERENDER and can report a clean boot it never observed.
//
// So this proves hydration happened before it reports anything about it — it waits for the runtime to
// announce itself, fails loudly if it never does, and samples the document every 50ms from before the
// first byte of page script until well after mount. What it prints is a timeline: the frames where the
// body's markup, its height, or its painted colours changed, plus every mutation the morph made to the
// document element.
//
// Usage (from this directory):
//   dotnet run hydrateprobe.cs http://127.0.0.1:PORT [/path]

using System.Text.Json;
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5090";
string[] paths = args.Length > 1 ? [args[1]] : ["/", "/docs"];

// Runs before any page script. Sampling starts immediately, so the first sample is the prerendered
// document as the parser left it — the thing a reader actually sees first.
const string Instrument = """
window.__h = {
  samples: [], htmlAttrs: [], bodyMutations: 0, bodyReplaced: 0, errors: [],
  removed: [], added: [],
  start: performance.now(), booted: null,
};

const sample = () => {
  const b = document.body;
  if (!b) { return; }
  const cs = getComputedStyle(b);
  window.__h.samples.push({
    t: Math.round(performance.now()),
    len: b.innerHTML.length,
    kids: b.childElementCount,
    h: document.documentElement.scrollHeight,
    bg: cs.backgroundColor,
    fg: cs.color,
    fam: cs.fontFamily.slice(0, 18),
  });
};

// Node IDENTITY across the handover. "Removed" in a mutation record is ambiguous — an atomic
// moveBefore() reports a removal too, and a moved <link> keeps its sheet applied. Whether <head> and
// <body> are the SAME ELEMENTS afterwards is not ambiguous: if they are new, everything in them was
// detached and re-created, and a detached stylesheet is an unstyled frame.
window.__h.pin = () => {
  window.__h.headNode = document.head;
  window.__h.bodyNode = document.body;
  window.__h.firstSheet = document.querySelector('link[rel=stylesheet]');
};
window.__h.identity = () => ({
  head: window.__h.headNode === document.head,
  body: window.__h.bodyNode === document.body,
  sheet: window.__h.firstSheet === document.querySelector('link[rel=stylesheet]'),
  sheetConnected: window.__h.firstSheet ? window.__h.firstSheet.isConnected : null,
  sheets: document.styleSheets.length,
});
if (document.head) { window.__h.pin(); }
else { document.addEventListener('DOMContentLoaded', () => window.__h.pin()); }

sample();
const timer = setInterval(sample, 50);
setTimeout(() => clearInterval(timer), 15000);

new MutationObserver((records) => {
  for (const r of records) {
    if (r.target === document.documentElement) {
      if (r.type === 'attributes') {
        window.__h.htmlAttrs.push({
          t: Math.round(performance.now()),
          name: r.attributeName,
          from: r.oldValue,
          to: document.documentElement.getAttribute(r.attributeName),
        });
      }
      for (const n of r.removedNodes) { if (n.nodeName === 'BODY') { window.__h.bodyReplaced++; } }
    } else {
      window.__h.bodyMutations++;
      const note = (list, n) => {
        // Only the hydration churn. Everything before this is the PARSER building the prerendered
        // document, which fills the cap with noise and hides the handover this probe exists to see.
        if (performance.now() < 200 || list.length >= 40) { return; }
        list.push({
          t: Math.round(performance.now()),
          what: (n.outerHTML || ('#text:' + String(n.textContent).trim().slice(0, 40))).slice(0, 150),
          parent: r.target.nodeName + (r.target.id ? '#' + r.target.id : ''),
        });
      };
      for (const n of r.removedNodes) { note(window.__h.removed, n); }
      for (const n of r.addedNodes) { note(window.__h.added, n); }
    }
  }
// `document`, not `document.documentElement`: an init script runs before the document element exists, so
// observing it threw and every mutation count below was silently zero.
}).observe(document, {
  attributes: true, attributeOldValue: true, childList: true, subtree: true, characterData: true,
});

// The runtime's own signal. Rask's WASM host calls this hook after it has mounted and morphed; if it
// never fires, nothing below is a statement about hydration.
const prev = window.raskAfterMorph;
window.raskAfterMorph = function () {
  if (window.__h.booted === null) { window.__h.booted = Math.round(performance.now()); }
  sample();
  if (typeof prev === 'function') { prev(); }
};

addEventListener('error', (e) => window.__h.errors.push(String(e.message)));
""";

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });

foreach (var path in paths)
{
    await using var ctx = await browser.NewContextAsync();
    var page = await ctx.NewPageAsync();
    var console = new List<string>();

    page.Console += (_, m) =>
    {
        if (m.Type is "error" or "warning")
        {
            console.Add($"{m.Type}: {m.Text}");
        }
    };

    page.PageError += (_, e) => console.Add("pageerror: " + e);

    await page.AddInitScriptAsync(Instrument);
    await page.GotoAsync(baseUrl + path, new() { Timeout = 120_000 });

    // Interactivity, not markup: the prerendered page already has every element, so the only honest
    // "it booted" signal is the runtime hook firing.
    var booted = true;
    try
    {
        await page.WaitForFunctionAsync("() => window.__h.booted !== null", null, new() { Timeout = 60_000 });
    }
    catch (TimeoutException)
    {
        booted = false;
    }

    await page.WaitForTimeoutAsync(3000);

    var probe = await page.EvaluateAsync<JsonElement>("() => window.__h");

    Console.WriteLine($"=== {path} ===");
    Console.WriteLine(booted
        ? $"  hydrated at {probe.GetProperty("booted").GetInt32()}ms"
        : "  NEVER HYDRATED — the runtime's raskAfterMorph hook did not fire inside 60s. Everything "
          + "below describes the prerendered document only.");
    var identity = await page.EvaluateAsync<JsonElement>("() => window.__h.identity()");
    Console.WriteLine(
        $"  same <head> element    : {identity.GetProperty("head").GetBoolean()}"
        + $"   same <body>: {identity.GetProperty("body").GetBoolean()}");
    Console.WriteLine(
        $"  first stylesheet <link>: same node {identity.GetProperty("sheet").GetBoolean()}"
        + $", original still connected {identity.GetProperty("sheetConnected")}"
        + $", document.styleSheets now {identity.GetProperty("sheets").GetInt32()}");
    Console.WriteLine($"  body subtree mutations : {probe.GetProperty("bodyMutations").GetInt32()}");
    Console.WriteLine($"  <body> replaced        : {probe.GetProperty("bodyReplaced").GetInt32()}x");

    foreach (var a in probe.GetProperty("htmlAttrs").EnumerateArray())
    {
        Console.WriteLine(
            $"  <html> {a.GetProperty("t").GetInt32(),5}ms {a.GetProperty("name").GetString()}: "
            + $"{Str(a, "from")} -> {Str(a, "to")}");
    }

    // Only the frames that CHANGED. A timeline of identical samples says nothing; the transitions are
    // the flicker, and their timestamps say whether a reader could see them.
    Console.WriteLine("  transitions (markup length / children / height / background / text / font):");

    JsonElement? last = null;
    var shown = 0;

    foreach (var s in probe.GetProperty("samples").EnumerateArray())
    {
        if (last is { } p && Same(p, s))
        {
            continue;
        }

        Console.WriteLine(
            $"    {s.GetProperty("t").GetInt32(),6}ms  len={s.GetProperty("len").GetInt32(),-7}"
            + $"kids={s.GetProperty("kids").GetInt32(),-4}h={s.GetProperty("h").GetInt32(),-6}"
            + $"bg={s.GetProperty("bg").GetString(),-26}fg={s.GetProperty("fg").GetString(),-26}"
            + $"{s.GetProperty("fam").GetString()}");

        last = s;
        shown++;
    }

    Console.WriteLine($"  {shown} distinct frame(s) out of {probe.GetProperty("samples").GetArrayLength()} samples");

    foreach (var label in new[] { "removed", "added" })
    {
        foreach (var n in probe.GetProperty(label).EnumerateArray())
        {
            Console.WriteLine(
                $"  {label,-8}{n.GetProperty("t").GetInt32(),6}ms from {n.GetProperty("parent").GetString()}: "
                + n.GetProperty("what").GetString());
        }
    }

    foreach (var e in console.Take(12))
    {
        Console.WriteLine("  console " + e);
    }

    Console.WriteLine();
}

static bool Same(JsonElement a, JsonElement b) =>
    a.GetProperty("len").GetInt32() == b.GetProperty("len").GetInt32()
    && a.GetProperty("kids").GetInt32() == b.GetProperty("kids").GetInt32()
    && a.GetProperty("h").GetInt32() == b.GetProperty("h").GetInt32()
    && a.GetProperty("bg").GetString() == b.GetProperty("bg").GetString()
    && a.GetProperty("fg").GetString() == b.GetProperty("fg").GetString()
    && a.GetProperty("fam").GetString() == b.GetProperty("fam").GetString();

static string Str(JsonElement o, string key) =>
    o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "(null)";
