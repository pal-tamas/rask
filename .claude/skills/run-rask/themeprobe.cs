#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Theme + first-paint probe for the Rask site.
//
// Three questions no unit test can answer, because all three are about what a BROWSER does with the
// shipped sheets:
//
//   1. Does the page follow prefers-color-scheme when the reader has chosen nothing? The boot script
//      writes NO data-theme in that case and leans on daisyUI's own
//      `[data-rask-ui]:not([data-theme])` under the dark media query. Whether that actually paints is
//      a cascade question, and arithmetic over the CSS cannot see it.
//   2. Do the kit's per-theme corrections WIN? They live in `@layer rask` in ui.css and correct tokens
//      the app's own @theme also declares at :root, in a different <link>. Same specificity either
//      way, so only the layer order decides — and layer order across separate sheets is exactly the
//      thing this repo has been bitten by before.
//   3. What flickers on first paint? Reported on / and /docs. Instrumented rather than eyeballed:
//      every <html> attribute mutation, every <body> replacement, the paint timings, and the layout
//      shifts, with their sources.
//
// Usage (from this directory):
//   dotnet run themeprobe.cs http://127.0.0.1:PORT

using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5050";

// Installed BEFORE any document script, so it sees the boot script's own first mutation. Everything it
// records is read back after the app has mounted.
const string Instrument = """
window.__probe = { attrs: [], bodies: 0, shifts: [], t0: performance.now() };

new MutationObserver(function (records) {
  for (const r of records) {
    if (r.type === 'attributes') {
      window.__probe.attrs.push({
        at: Math.round(performance.now()),
        name: r.attributeName,
        from: r.oldValue,
        to: document.documentElement.getAttribute(r.attributeName),
      });
    }
    for (const n of r.addedNodes) {
      if (n.nodeName === 'BODY') { window.__probe.bodies++; }
    }
  }
}).observe(document.documentElement, {
  attributes: true, attributeOldValue: true, childList: true,
});

new PerformanceObserver(function (list) {
  for (const e of list.getEntries()) {
    if (!e.hadRecentInput && e.value > 0.001) {
      window.__probe.shifts.push({
        at: Math.round(e.startTime),
        value: Number(e.value.toFixed(4)),
        sources: (e.sources || []).map(s => s.node ? (s.node.nodeName + '.' +
          (s.node.className && s.node.className.toString ? s.node.className.toString().slice(0, 40) : '')) : '?'),
      });
    }
  }
}).observe({ type: 'layout-shift', buffered: true });
""";

// Resolves a custom property to the rgb() a browser computes, by letting the browser do it: the value
// of --color-ui-muted is a color-mix() expression, and only `getComputedStyle` on a property that
// CONSUMES it reports the colour. Reading the custom property itself returns the unresolved text.
const string ResolveFn = """
(names) => {
  // Chromium reports a computed colour in the space it was AUTHORED in — `oklch(0.2533 0.016 252.42)`
  // for a daisyUI token, `oklab(...)` for a color-mix() result — so the value does not parse as rgb and
  // a caller that assumes it does scores NaN in silence. A 1x1 canvas converts it the way the
  // compositor would, which is the only form a contrast ratio may be computed from.
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 1;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });

  const toRgb = (value) => {
    ctx.clearRect(0, 0, 1, 1);
    ctx.fillStyle = '#000';
    ctx.fillStyle = value;
    ctx.fillRect(0, 0, 1, 1);
    const d = ctx.getImageData(0, 0, 1, 1).data;
    return 'rgb(' + d[0] + ', ' + d[1] + ', ' + d[2] + ')';
  };

  const probe = document.createElement('span');
  probe.style.position = 'fixed';
  probe.style.opacity = '0';
  document.body.appendChild(probe);
  const out = {};
  for (const n of names) {
    probe.style.color = 'var(' + n + ')';
    out[n] = toRgb(getComputedStyle(probe).color);
  }
  out['__html-bg'] = toRgb(getComputedStyle(document.documentElement).backgroundColor);
  out['__body-bg'] = toRgb(getComputedStyle(document.body).backgroundColor);
  out['__data-theme'] = document.documentElement.getAttribute('data-theme');
  out['__color-scheme'] = getComputedStyle(document.documentElement).colorScheme;
  probe.remove();
  return out;
}
""";

string[] tokens =
[
    "--color-ui-bg", "--color-ui-well", "--color-ui-ink", "--color-ui-muted",
    "--color-ui-brand-ink", "--color-ui-ok-ink", "--color-ui-warn-ink",
];

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });

// ---- 1. the OS decides, in both directions ------------------------------------------------------

foreach (var scheme in new[] { ColorScheme.Light, ColorScheme.Dark })
{
    await using var ctx = await browser.NewContextAsync(new() { ColorScheme = scheme });
    var page = await ctx.NewPageAsync();
    await page.GotoAsync(baseUrl, new() { Timeout = 90_000 });
    await page.WaitForSelectorAsync("h1", new() { Timeout = 90_000 });

    var values = await page.EvaluateAsync<JsonElement>(ResolveFn, tokens);
    Console.WriteLine($"== prefers-color-scheme: {scheme.ToString()!.ToLowerInvariant()} ==");
    Console.WriteLine($"   data-theme      : {Show(values, "__data-theme")}");
    Console.WriteLine($"   color-scheme    : {Show(values, "__color-scheme")}");
    Console.WriteLine($"   html background : {Show(values, "__html-bg")}");
    Console.WriteLine($"   --color-ui-ink  : {Show(values, "--color-ui-ink")}");
    Console.WriteLine($"   --color-ui-muted: {Show(values, "--color-ui-muted")}");
    Console.WriteLine($"   contrast ink/bg : {Ratio(Show(values, "--color-ui-ink"), Show(values, "__html-bg")):0.00}:1");
    Console.WriteLine($"   contrast muted  : {Ratio(Show(values, "--color-ui-muted"), Show(values, "__html-bg")):0.00}:1");
}

// ---- 2. the per-theme corrections win -----------------------------------------------------------

Console.WriteLine();

foreach (var theme in new[] { "valentine", "retro", "dark", "luxury" })
{
    await using var ctx = await browser.NewContextAsync();
    var page = await ctx.NewPageAsync();
    await page.GotoAsync(baseUrl, new() { Timeout = 90_000 });
    await page.WaitForSelectorAsync("h1", new() { Timeout = 90_000 });
    await page.EvaluateAsync("t => window.raskSetTheme(t)", theme);

    var values = await page.EvaluateAsync<JsonElement>(ResolveFn, tokens);
    var ink = Show(values, "--color-ui-ink");
    var bg = Show(values, "__html-bg");

    Console.WriteLine($"== {theme} (picked) ==");
    Console.WriteLine($"   data-theme       : {Show(values, "__data-theme")}");
    Console.WriteLine($"   ground           : {bg}");

    foreach (var token in tokens.Where(t => t != "--color-ui-bg" && t != "--color-ui-well"))
    {
        var value = Show(values, token);
        Console.WriteLine(
            $"   {token,-22}{value,-22} {Ratio(value, bg):0.00}:1"
            + (Ratio(value, bg) < 4.5 ? "   << UNDER AA" : string.Empty));
    }

    // The correction's fingerprint: on valentine and retro, muted is pulled much closer to the ink than
    // the 80% default would put it. Equal-ish to the ink means the @layer rask block is winning.
    if (theme is "valentine" or "retro")
    {
        // The fingerprint of the correction IS the ratio, which is the whole point of it: the 80%
        // default cannot clear AA on these two palettes and the override can. Comparing the colour to a
        // re-derived expectation would only re-test this file's own arithmetic.
        var muted = Show(values, "--color-ui-muted");
        var ratio = Ratio(muted, bg);
        Console.WriteLine(
            $"   correction applied : {(ratio >= 4.5 ? "YES" : "NO — the @layer rask override lost the cascade")}"
            + $" (muted {muted} at {ratio:0.00}:1, ink {ink})");
    }
}

// ---- 3. what actually flickers ------------------------------------------------------------------

Console.WriteLine();

foreach (var path in new[] { "/", "/docs" })
{
    await using var ctx = await browser.NewContextAsync();
    var page = await ctx.NewPageAsync();
    await page.AddInitScriptAsync(Instrument);
    await page.GotoAsync(baseUrl + path, new() { Timeout = 90_000 });
    await page.WaitForSelectorAsync("h1", new() { Timeout = 90_000 });
    await page.WaitForTimeoutAsync(2500);

    var probe = await page.EvaluateAsync<JsonElement>("""
        () => ({
            attrs: window.__probe.attrs,
            bodies: window.__probe.bodies,
            shifts: window.__probe.shifts,
            cls: window.__probe.shifts.reduce((a, s) => a + s.value, 0),
            paint: performance.getEntriesByType('paint').map(e => ({ name: e.name, at: Math.round(e.startTime) })),
            height: document.documentElement.scrollHeight,
        })
        """);

    Console.WriteLine($"== first paint: {path} ==");

    foreach (var p in probe.GetProperty("paint").EnumerateArray())
    {
        Console.WriteLine($"   {p.GetProperty("name").GetString(),-24}{p.GetProperty("at").GetInt32()}ms");
    }

    Console.WriteLine($"   <body> replaced         {probe.GetProperty("bodies").GetInt32()}x");
    Console.WriteLine($"   <html> attr mutations   {probe.GetProperty("attrs").GetArrayLength()}");

    foreach (var a in probe.GetProperty("attrs").EnumerateArray())
    {
        Console.WriteLine(
            $"      {a.GetProperty("at").GetInt32(),5}ms {a.GetProperty("name").GetString()}: "
            + $"{Str(a, "from")} -> {Str(a, "to")}");
    }

    Console.WriteLine($"   cumulative layout shift {probe.GetProperty("cls").GetDouble():0.0000}");

    foreach (var s in probe.GetProperty("shifts").EnumerateArray())
    {
        var sources = string.Join(", ", s.GetProperty("sources").EnumerateArray().Select(x => x.GetString()));
        Console.WriteLine($"      {s.GetProperty("at").GetInt32(),5}ms {s.GetProperty("value").GetDouble():0.0000}  {sources}");
    }
}

static string Show(JsonElement o, string key) =>
    o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "(none)";

static string Str(JsonElement o, string key) =>
    o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "(null)";

// WCAG 2.x over the rgb() strings the browser reports, so the number is measured on the real
// composited colour rather than on an expression this file re-derives.
static double Ratio(string a, string b)
{
    var (x, y) = (Luminance(a), Luminance(b));
    return x < 0 || y < 0 ? double.NaN : (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
}

static double Luminance(string rgb)
{
    var digits = rgb.Split('(', ')');
    if (digits.Length < 2)
    {
        return -1;
    }

    var parts = digits[1].Split(',', '/');
    if (parts.Length < 3)
    {
        return -1;
    }

    double Channel(string s)
    {
        var v = double.Parse(s.Trim(), CultureInfo.InvariantCulture) / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    return (0.2126 * Channel(parts[0])) + (0.7152 * Channel(parts[1])) + (0.0722 * Channel(parts[2]));
}
