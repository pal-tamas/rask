#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// The PAINTED frames of a page load, as pixels — for the flicker a computed-style sample cannot see.
//
// CDP's screencast hands over every frame the compositor produces. Consecutive identical frames are
// dropped by content hash, so what lands in ./screenshots/paint/ is exactly the sequence of DIFFERENT
// pictures a reader saw, each named by its offset from navigation. Open them in order.
//
// Usage (from this directory):
//   dotnet run paintprobe.cs https://rask.sh [/path] [width] [height]

using System.Security.Cryptography;
using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://localhost:5090";
string path = args.Length > 1 ? args[1] : "/";
int width = args.Length > 2 ? int.Parse(args[2]) : 1280;
int height = args.Length > 3 ? int.Parse(args[3]) : 900;
var scheme = args.Length > 4 && args[4] == "dark" ? ColorScheme.Dark : ColorScheme.Light;
string? storedTheme = args.Length > 5 && args[5] != "-" ? args[5] : null;
int throttle = args.Length > 6 ? int.Parse(args[6]) : 1;

var slug = path.Trim('/') is "" ? "root" : path.Trim('/').Replace('/', '-');
var outDir = Path.Combine("screenshots", "paint", $"{slug}-{width}-{scheme}-{storedTheme ?? "os"}-x{throttle}".ToLowerInvariant());
if (Directory.Exists(outDir))
{
    Directory.Delete(outDir, recursive: true);
}

Directory.CreateDirectory(outDir);

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });
await using var ctx = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = height }, ColorScheme = scheme });
var page = await ctx.NewPageAsync();
var cdp = await ctx.NewCDPSessionAsync(page);
if (throttle > 1)
{
    await cdp.SendAsync("Emulation.setCPUThrottlingRate", new() { ["rate"] = throttle });
}

if (storedTheme is not null)
{
    // A returning reader who picked a theme has it in localStorage (UiThemeScript's default key).
    await page.AddInitScriptAsync($"try {{ localStorage.setItem('rask-theme', '{storedTheme}'); }} catch {{ }}");
}

var started = DateTime.UtcNow;
string? lastHash = null;
var kept = 0;
var total = 0;
var booted = -1.0;

page.Console += (_, m) =>
{
    if (m.Text.StartsWith("__booted", StringComparison.Ordinal))
    {
        booted = (DateTime.UtcNow - started).TotalMilliseconds;
    }
};

cdp.Event("Page.screencastFrame").OnEvent += async (_, e) =>
{
    if (e is null)
    {
        return;
    }

    var session = e.Value.GetProperty("sessionId").GetInt32();
    var data = e.Value.GetProperty("data").GetString()!;
    var ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
    total++;
    var hash = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(data)))[..12];
    if (hash != lastHash)
    {
        lastHash = hash;
        kept++;
        await File.WriteAllBytesAsync(Path.Combine(outDir, $"{ms:D5}ms-{hash}.png"), Convert.FromBase64String(data));
    }

    await cdp.SendAsync("Page.screencastFrameAck", new() { ["sessionId"] = session });
};

await page.AddInitScriptAsync("""
const prev = window.raskAfterMorph;
let said = false;
window.raskAfterMorph = function () { if (!said) { said = true; console.log('__booted'); } if (typeof prev === 'function') prev.apply(this, arguments); };
""");
await cdp.SendAsync("Page.startScreencast", new() { ["format"] = "png", ["everyNthFrame"] = 1 });
started = DateTime.UtcNow;
await page.GotoAsync(baseUrl + path, new() { Timeout = 120_000 });
await page.WaitForTimeoutAsync(6000);
await cdp.SendAsync("Page.stopScreencast");

Console.WriteLine($"{baseUrl}{path} @{width}x{height}: {kept} distinct of {total} frames; first morph ~{booted:F0}ms -> {outDir}");
foreach (var f in Directory.GetFiles(outDir).Order())
{
    Console.WriteLine("  " + Path.GetFileName(f));
}
