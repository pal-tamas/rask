#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Navigate the showcase to one sidebar entry at PHONE width and name the elements that genuinely widen
// the document — an element wider than the viewport with NO scrolling ancestor to contain it. A code
// block inside `overflow-x: auto` is not a defect; the same block in a plain <div> is.
//
// Deep links 404 under WasmAppHost, so the route is reached the way a user reaches it: open the drawer,
// filter, click. Pass "-" as the label to stay on /docs.
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5050";
string label = args.Length > 1 ? args[1] : "Forms";

using var pw = await Playwright.CreateAsync();
await using var b = await pw.Chromium.LaunchAsync(new() { Headless = true });
var page = await b.NewPageAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
await page.GotoAsync(baseUrl + "/docs", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 90000 });
await page.WaitForTimeoutAsync(2000);

if (label != "-")
{
    var toggle = page.Locator(".hamburger-btn");
    if (await toggle.CountAsync() > 0 && await toggle.First.IsVisibleAsync())
    {
        await toggle.First.ClickAsync();
        await page.WaitForTimeoutAsync(500);
    }

    await page.Locator(".side-nav .side-nav-filter input").First.FillAsync(label);
    var any = page.Locator($".side-nav a.side-nav-link:has-text(\"{label}\")");
    await any.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
    await page.WaitForTimeoutAsync(300);
    await any.First.ClickAsync();
    await page.WaitForTimeoutAsync(1500);
}

Console.WriteLine("URL: " + page.Url);

var report = await page.EvaluateAsync<string>(@"() => {
  const vw = document.documentElement.clientWidth;
  const out = ['html scrollW=' + document.documentElement.scrollWidth + ' clientW=' + vw];
  const culprits = [];
  for (const el of document.querySelectorAll('*')) {
    const r = el.getBoundingClientRect();
    if (r.width === 0 || r.right <= vw + 1) continue;
    // Contained by a scrolling ancestor? Then it is scrollable content, not a layout defect.
    let contained = false;
    for (let n = el.parentElement; n; n = n.parentElement) {
      const ox = getComputedStyle(n).overflowX;
      if (ox === 'auto' || ox === 'scroll' || ox === 'hidden') { contained = true; break; }
    }
    if (contained) continue;
    culprits.push({el, right: r.right, width: r.width});
  }
  culprits.sort((a, b) => b.right - a.right);
  const seen = new Set();
  out.push('--- elements that WIDEN the document (no scrolling ancestor) ---');
  for (const {el, right, width} of culprits) {
    const key = el.tagName + '.' + el.className;
    if (seen.has(key)) continue;
    seen.add(key);
    const cs = getComputedStyle(el);
    out.push('  right=' + Math.round(right) + ' w=' + Math.round(width) + '  <' + el.tagName.toLowerCase() +
      ' class=' + (el.className || '').toString().slice(0, 60) + '>  overflowX=' + cs.overflowX +
      ' whiteSpace=' + cs.whiteSpace + '  text=' + (el.textContent || '').trim().slice(0, 45).replace(/\s+/g, ' '));
    if (seen.size >= 10) break;
  }
  if (seen.size === 0) out.push('  (none — the page fits)');
  return out.join('\n');
}");
Console.WriteLine(report);

var shot = Path.Combine(Directory.GetCurrentDirectory(), "screenshots", "probe-" + label.ToLowerInvariant() + "-phone.png");
await page.ScreenshotAsync(new() { Path = shot, FullPage = true });
Console.WriteLine("shot -> " + shot);
