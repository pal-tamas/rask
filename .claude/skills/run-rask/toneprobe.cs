#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Do the kit's tone corrections actually WIN the cascade?
//
// The kit corrects daisyUI's filled components in `@layer rask-ui-corrections`, which is named in no order
// statement and so is appended after `utilities` — where daisyUI actually emits its component rules. That
// is the whole mechanism, and no arithmetic over the stylesheet can check it: the overrides are lower
// specificity than the rules they correct, so a wrong layer loses silently while every contrast number
// computed from the tokens still looks right. This probe is what caught exactly that: written in `rask`
// (which the statement puts BEFORE `utilities`), `.btn-primary` measured 3.29:1 on `corporate` with the
// correction present in the shipped bytes and the whole unit suite green.
//
// It INJECTS the components rather than navigating to a page that renders them. The question is about CSS,
// not about markup, and a probe that has to find a real `.btn-primary` in the showcase ends up asserting
// the sidebar instead: those rows are contributed by the host at boot, so they are not in the shell, and a
// deep link cannot be used either (WasmAppHost installs no SPA fallback). Injecting one element per tone
// tests exactly the thing in question and nothing else.
//
// Usage (from this directory):
//   dotnet run toneprobe.cs http://127.0.0.1:PORT

using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5050";

string[] themes = ["(system)", "dark", "pastel", "valentine", "winter", "corporate", "luxury", "retro", "silk"];
string[] tones = ["primary", "secondary", "accent", "neutral", "info", "success", "warning", "error"];

// One element per tone per family, appended to the document under the theme scope, measured, removed.
const string Probe = """
([families, tones]) => {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 1;
  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  const lum = (value) => {
    ctx.fillStyle = '#000';
    ctx.fillStyle = value;
    ctx.fillRect(0, 0, 1, 1);
    const d = ctx.getImageData(0, 0, 1, 1).data;
    const ch = (v) => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
    return 0.2126 * ch(d[0]) + 0.7152 * ch(d[1]) + 0.0722 * ch(d[2]);
  };

  const host = document.createElement('div');
  document.body.appendChild(host);
  const out = [];

  for (const family of families) {
    for (const tone of tones) {
      const el = document.createElement(family === 'btn' ? 'button' : 'span');
      el.className = family + ' ' + family + '-' + tone;
      el.textContent = 'Label';
      host.appendChild(el);

      const cs = getComputedStyle(el);
      let bg = cs.backgroundColor;
      let node = el;
      while (node && (bg === 'rgba(0, 0, 0, 0)' || bg === 'transparent')) {
        node = node.parentElement;
        if (!node) { break; }
        bg = getComputedStyle(node).backgroundColor;
      }

      const f = lum(cs.color), b = lum(bg);
      out.push({
        what: family + '-' + tone,
        ratio: (Math.max(f, b) + 0.05) / (Math.min(f, b) + 0.05),
        fg: cs.color,
        bg: bg,
      });
      el.remove();
    }
  }

  host.remove();
  return out;
};
""";

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();

await page.GotoAsync(baseUrl + "/docs", new() { Timeout = 120_000 });
await page.WaitForSelectorAsync("body", new() { Timeout = 120_000 });

// The theme scope has to be on the document, or every daisyUI token resolves to nothing and the numbers
// below would be measuring an unstyled page rather than a palette.
var scoped = await page.EvaluateAsync<bool>("() => document.documentElement.hasAttribute('data-rask-ui')");
if (!scoped)
{
    Console.WriteLine("the document carries no data-rask-ui — nothing here would mean anything.");
    return 1;
}

var failures = 0;

foreach (var theme in themes)
{
    await page.EvaluateAsync("t => window.raskSetTheme(t)", theme == "(system)" ? "system" : theme);

    var rows = await page.EvaluateAsync<JsonElement>(Probe, new object[] { new[] { "btn", "badge" }, tones });
    var worst = (Ratio: 99.0, What: "");

    foreach (var row in rows.EnumerateArray())
    {
        var ratio = row.GetProperty("ratio").GetDouble();
        var what = row.GetProperty("what").GetString()!;

        if (ratio < worst.Ratio)
        {
            worst = (ratio, what);
        }

        if (ratio < 4.5)
        {
            failures++;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {theme,-10} {what,-18} {ratio,5:0.00}:1  UNDER AA   "
                + $"fg={row.GetProperty("fg").GetString()}  bg={row.GetProperty("bg").GetString()}"));
        }
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"  {theme,-10} worst {worst.Ratio,5:0.00}:1  ({worst.What})"));
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? "every tone clears AA in every palette measured — @layer rask-ui-corrections wins the cascade."
    : $"{failures} pair(s) under AA — the corrections are not reaching the rendered components.");

return failures == 0 ? 0 : 1;
