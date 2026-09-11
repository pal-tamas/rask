#:package Microsoft.Playwright
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Screenshots the built-in operator console (Rask.Dashboard) out of a throwaway `rask new` app — the
// only place it is mounted now that samples/ is gone, and the honest subject anyway, since a scaffolded
// app is what a user mounts it in.
//
// The console is behind a policy — the scaffold gates it on the ADMIN role — so this signs in first. A
// fresh app has no accounts, so the FIRST run has to register one, and the accounts battery gates that
// first registration on a one-time first-run token it logs at startup (Rask.Auth's
// FirstRunTokenInitializer, at Warning). Pass the token as the second argument and this registers; omit
// it and this signs in with an existing account. Either way the first account becomes the administrator,
// which is what the console's policy wants.
//
// Shoots every page twice — 1280px and 390px — because the console is built mobile-first and the two
// layouts are genuinely different markup paths (columns drop, the leader rules disappear, the sheet
// becomes a bottom sheet). One width proves nothing about the other.
//
// Usage (run from THIS directory so screenshots land in ./screenshots/):
//   dotnet run dashboard-driver.cs [baseUrl] [firstRunToken]
//
// SKILL.md has the whole recipe, including how to build the throwaway app against THIS working tree and
// how to read the token out of its log.

using Microsoft.Playwright;

string baseUrl = args.Length > 0 ? args[0] : "http://localhost:5123";
string? firstRunToken = args.Length > 1 ? args[1] : null;
const string Email = "ops@example.com";
const string Password = "Passw0rd!ops";
string shotDir = Path.Combine(Directory.GetCurrentDirectory(), "screenshots");
Directory.CreateDirectory(shotDir);

// The console's OWN markup, not the host app's auth chrome. `.rask-ops` is UiShell's documented hook and
// the outermost thing every console page renders; the nav is the layout's tab bar. Waiting on the host's
// sign-out control instead is what left this driver shooting nothing at all: the scaffold's home page
// renders no such control, so the wait burned its timeout BEFORE the first screenshot and the whole run
// produced no images — a broken tool reporting a fault in the thing it was inspecting.
const string ConsoleShell = "div.rask-ops";
const string ConsoleNav = ConsoleShell + " nav a:has-text('Overview')";

// The relay that actually mints the session. A component handler runs on the WebSocket and a WebSocket
// cannot write a Set-Cookie, so Rask parks the sign-in, hands the browser a single-use ticket, and the
// browser redeems it over ordinary HTTP at this path (rask.ts redeemAuthTicket → RaskEndpointExtensions).
// Waiting for THAT response is what "signed in" means here: the URL leaves /login before the cookie
// exists, so anything that navigates on the URL alone races straight back to /login.
const string RedeemPath = "/_rask/auth/redeem";

// A full-page PNG of a rendered console runs to tens of KB at either width; a blank one is still a few.
// This floor only has to catch "no file / zero bytes", which is what a silent failure looks like.
const int MinShotBytes = 1024;

(string path, string name)[] pages =
[
    ("/_rask", "ops-overview"),
    ("/_rask/queues/jobs", "ops-queue"),
    ("/_rask/cache", "ops-cache"),
    ("/_rask/storage", "ops-storage"),
    ("/_rask/logs?view=history", "ops-logs"),
    ("/_rask/system", "ops-system"),
];

(int w, int h, string tag)[] sizes = [(1280, 900, "desktop"), (390, 844, "mobile")];

var shots = new List<string>();

using var pw = await Playwright.CreateAsync();
await using var browser = await pw.Chromium.LaunchAsync(new() { Headless = true });

foreach (var (w, h, tag) in sizes)
{
    await using var ctx = await browser.NewContextAsync(new()
    {
        BaseURL = baseUrl,
        ViewportSize = new() { Width = w, Height = h },
    });
    var page = await ctx.NewPageAsync();

    // Claim the app on the first pass only: the account persists in the app's database, so registering
    // twice would fail on the mobile pass with "that email is already taken".
    var claim = firstRunToken is not null && tag == "desktop";
    await SignInAsync(page, claim ? firstRunToken : null);

    Console.WriteLine($"── {tag} ({w}x{h}) ────────────────────────────");

    foreach (var (path, name) in pages)
    {
        var res = await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 25000 });

        // The console's own chrome, so a bounce back to /login fails here with the URL in the message
        // rather than yielding a screenshot of the sign-in page.
        try
        {
            await page.Locator(ConsoleNav).First.WaitForAsync(new() { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                $"The console did not render at {path} — landed on {page.Url} (status {res?.Status}). "
                + $"No element matched '{ConsoleNav}'. Either the policy rejected this account (the "
                + "scaffold requires the Admin role, which only the FIRST registration gets), or the "
                + "console's shell markup moved and this selector needs re-deriving.");
        }

        // The panels load on PollingPanel's async mount, so the first paint is the spinner.
        await page.WaitForTimeoutAsync(900);

        var file = Path.Combine(shotDir, $"{name}-{tag}.png");
        await page.ScreenshotAsync(new() { Path = file, FullPage = true });
        RecordShot(file, path);

        // The one failure that makes a phone layout unusable rather than merely ugly.
        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");

        // And the tables separately: they carry their own overflow-x as a backstop, so a column that
        // failed to collapse hides its content behind an internal scrollbar while the document check
        // above stays green.
        var wideTables = await page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('table')]"
            + ".filter(t => t.scrollWidth > t.parentElement.clientWidth + 1).length");

        var flag = overflow ? "PAGE-OVERFLOW" : wideTables > 0 ? $"TABLE-WIDE({wideTables})" : "ok";
        Console.WriteLine($"  {flag,-15} {path,-28} {res!.Status} -> {name}-{tag}.png");
    }
}

// The run is only worth anything if it produced images. Anything short of the full set is a defect in the
// driver or the console, and saying so beats a green exit over an empty directory.
var expected = pages.Length * sizes.Length;
if (shots.Count != expected)
{
    throw new InvalidOperationException(
        $"Expected {expected} screenshots ({pages.Length} pages x {sizes.Length} widths) but wrote {shots.Count}.");
}

Console.WriteLine($"\n{shots.Count} screenshots in {shotDir}");

// Proves an image was actually produced. A driver whose only output is a directory listing can pass while
// writing nothing — the failure mode this whole file exists to make impossible.
void RecordShot(string file, string path)
{
    var info = new FileInfo(file);
    if (!info.Exists || info.Length < MinShotBytes)
    {
        throw new InvalidOperationException(
            $"Screenshot of {path} is missing or empty: {file} "
            + $"({(info.Exists ? info.Length + " bytes" : "no file")}, expected at least {MinShotBytes}).");
    }

    shots.Add(file);
}

// Registers (claiming the app) or signs in, then waits for the session to actually exist.
async Task SignInAsync(IPage page, string? token)
{
    var (formPath, submit) = token is null ? ("/login", "#login-submit") : ("/register", "#register-submit");
    await page.GotoAsync(formPath, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
    await page.FillAsync("#email", Email);
    await page.FillAsync("#password", Password);

    if (token is not null)
    {
        // The field is rendered only while the app is unclaimed (RegisterPage reads FirstRunToken
        // .IsPending), so its absence means the app already has an account — drop the token argument.
        var field = page.Locator("#first-run-token");
        if (await field.CountAsync() == 0)
        {
            throw new InvalidOperationException(
                "A first-run token was passed but /register shows no #first-run-token field, so this app "
                + "is already claimed. Re-run without the token to sign in, or start from a fresh app.");
        }

        await field.FillAsync(token);
    }

    try
    {
        await page.RunAndWaitForResponseAsync(
            () => page.ClickAsync(submit),
            r => r.Url.Contains(RedeemPath, StringComparison.Ordinal) && r.Status == 200,
            new() { Timeout = 20000 });
    }
    catch (TimeoutException)
    {
        // The auth pages render their own reason — "that email is already taken", "must be at least 8
        // characters" — which is far more useful than "timed out".
        string? reason = null;
        try
        {
            reason = await page.Locator("#login-error, #register-error").First
                .TextContentAsync(new() { Timeout = 2000 });
        }
        catch (TimeoutException)
        {
            // No error banner: the form was accepted and the relay itself is what failed.
        }

        throw new InvalidOperationException(
            $"Sign-in never completed: no 200 from {RedeemPath} within 20s (still at {page.Url})."
            + (reason is null ? "" : $" The page says: {reason.Trim()}"));
    }
}
