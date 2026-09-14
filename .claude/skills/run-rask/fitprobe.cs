#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Does the landing page's Counter.cs window fit its code, at every hero width, in BOTH the web font and the
// fallback a cold load paints first? Prints the overflow of the <pre> (0 = nothing to scroll).
//   dotnet run fitprobe.cs http://127.0.0.1:5091 [engine]
using Microsoft.Playwright;

string baseUrl = args[0].TrimEnd('/');
string engine = args.Length > 1 ? args[1] : "webkit";
// Optional CSS injected after load, to try a layout before editing the source.
string? css = args.Length > 2 ? args[2] : null;
using var pw = await Playwright.CreateAsync();
await using var browser = await (engine == "webkit" ? pw.Webkit : pw.Chromium).LaunchAsync(new() { Headless = true });
int failures = 0;
foreach (var fonts in new[] { "webfont", "fallback" })
{
    foreach (var width in new[] { 360, 390, 768, 1023, 1024, 1060, 1100, 1180, 1280, 1440, 1920 })
    {
        await using var ctx = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        var page = await ctx.NewPageAsync();
        if (fonts == "fallback")
        {
            await page.RouteAsync("**/fonts/**", route => route.AbortAsync());
        }

        await page.GotoAsync(baseUrl + "/", new() { Timeout = 120_000 });
        if (css is not null)
        {
            await page.AddStyleTagAsync(new() { Content = css });
        }

        await page.WaitForTimeoutAsync(300);
        var m = await page.EvaluateAsync<string>("""
() => {
  const pre = [...document.querySelectorAll('pre')].find(p => p.textContent.includes('class Counter'));
  const code = pre.firstElementChild, cs = getComputedStyle(code);
  const h1 = document.querySelector('h1');
  const doc = document.documentElement;
  return [pre.scrollWidth - pre.clientWidth, pre.clientWidth, cs.fontSize, cs.fontFamily.slice(0, 14),
          'h1=' + Math.round(h1.getBoundingClientRect().height), 'page=' + (doc.scrollWidth - doc.clientWidth)].join(' ');
}
""");
        var over = int.Parse(m.Split(' ')[0]);
        Console.WriteLine($"{engine} {fonts,-8} {width,5}px  overflow={m}");
        if (width >= 768 && over > 0)
        {
            failures++;
        }
    }
}

Console.WriteLine(failures == 0 ? "FITS at every width >= 768" : $"{failures} width(s) >= 768 still scroll");
