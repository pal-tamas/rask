using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// The password-reset round trip against Rask.Example.Auth, through the parts no unit test can reach:
// the link is built by the running host from the real request origin, carried by a real email that the
// mail battery writes to disk, and consumed by a browser that only ever saw the .eml.
//
// The unit suite proves the store's behaviour. What it cannot prove is that the token SURVIVES the
// journey — that the link the email carries is absolute, that its query round-trips a base64 token
// through an HTML anchor, and that the page on the far end can read it back. Every one of those is a
// place a working reset silently becomes "that link has expired".
[Collection(AuthExampleCollection.Name)]
public sealed partial class AuthResetExampleTests : IAsyncLifetime
{
    private readonly AuthExampleAppFixture _app;
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;
    private IPage _page = default!;

    public AuthResetExampleTests(AuthExampleAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    public async Task InitializeAsync()
    {
        _ctx = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        _page = await _ctx.NewPageAsync();
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    // Ada's own password, reset to the value it already has.
    //
    // Deliberate: this fixture's app and its SQLite file are shared with AuthExampleTests, which signs
    // in as ada with this password, and xUnit gives no order within a collection. A reset to a NEW
    // password would make one of the two journeys fail depending on which ran first — a shared-state
    // flake that looks like a real auth bug. Setting it to the same value still exercises the whole
    // flow: a token is minted, emailed, consumed and rejected on replay.
    private const string Password = "Password1";

    [Fact]
    public async Task Journey_ForgotPassword_EmailedLink_ResetsAndSignsIn()
    {
        var pickups = MailPickupDirectories();
        var before = ExistingMail(pickups);

        // 1. From the sample's own /login to the FRAMEWORK's /forgot-password. The sample replaces
        //    /login with a page of its own, so this also proves that replacing one page leaves the
        //    rest of the flow routed.
        await _page.GotoAsync("/login");
        await Expect(_page.Locator("#go-forgot"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await _page.Locator("#go-forgot").ClickAsync();

        await Expect(_page).ToHaveURLAsync(new Regex(@"/forgot-password"),
            new PageAssertionsToHaveURLOptions { Timeout = 30_000 });

        // 2. Ask for the link.
        await _page.Locator("#email").FillAsync("ada@example.com");
        await _page.Locator("#forgot-submit").ClickAsync();
        await Expect(_page.Locator("#forgot-sent"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // 3. The email really arrives. With no SMTP configured the mail battery writes each message to
        //    ./mail-pickup as an .eml, which is also what a developer running the sample sees — so this
        //    reads the same artifact a person would open, rather than a test-only seam.
        var link = await WaitForResetLinkAsync(pickups, before);

        // Absolute, and pointing at the host that sent it: no PublicOrigin is configured here, so this
        // is the request-origin fallback doing its job. A relative href would have been invisible in
        // the queue and dead in an inbox.
        Assert.StartsWith(_app.BaseUrl, link, StringComparison.Ordinal);

        // 4. Follow it exactly as a mail client would — the browser has seen nothing else.
        await _page.GotoAsync(link);
        await Expect(_page.Locator("#reset-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await _page.Locator("#password").FillAsync(Password);
        await _page.Locator("#confirm").FillAsync(Password);
        await _page.Locator("#reset-submit").ClickAsync();

        await Expect(_page.Locator("#reset-done"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // 5. The link is spent. Identity rolls the security stamp on a completed reset, which is what
        //    invalidates the token — if this ever succeeds, a forwarded email stays live until it
        //    expires.
        //
        //    Asserted by SUBMITTING rather than by arriving: the page validates the token when it is
        //    used, not when it is opened, so a stale link still renders the form. That is the same
        //    shape as Identity's own default UI, and it is the security property that matters — what
        //    a dead token must not be able to do is CHANGE the password.
        await _page.GotoAsync(link);
        await Expect(_page.Locator("#reset-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await _page.Locator("#password").FillAsync("Replayed1password");
        await _page.Locator("#confirm").FillAsync("Replayed1password");
        await _page.Locator("#reset-submit").ClickAsync();

        await Expect(_page.Locator("#reset-error"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        // 6. And the account still signs in with the password from step 4 — which is also what proves
        //    the replayed submit above changed nothing, since it tried to set a different one.
        await _page.GotoAsync("/login");
        await _page.Locator("#email").FillAsync("ada@example.com");
        await _page.Locator("#password").FillAsync(Password);
        await _page.Locator("#login-submit").ClickAsync();

        await Expect(_page.Locator("#members-greeting"))
            .ToContainTextAsync("ada@example.com",
                new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
    }

    [Fact]
    public async Task An_unknown_address_is_answered_exactly_like_a_known_one()
    {
        await _page.GotoAsync("/forgot-password");
        await Expect(_page.Locator("#forgot-submit"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await _page.Locator("#email").FillAsync("nobody-at-all@example.com");
        await _page.Locator("#forgot-submit").ClickAsync();

        // The same "check your email" a registered address gets. A different message here — or a
        // visible error — would let anybody test a list of addresses against this app.
        await Expect(_page.Locator("#forgot-sent"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Expect(_page.Locator("#forgot-error")).ToHaveCountAsync(0);
    }

    // Where the running sample writes its pickup directory.
    //
    // The battery's PickupDirectory is the RELATIVE path "mail-pickup", so this depends on the app's
    // working directory rather than on the test's: `dotnet run --project X` runs the app with X as its
    // cwd, which is also why the sample's auth-sample.db sits beside its .csproj rather than at the
    // repo root. Both candidates are returned because a fixture running a PUBLISHED build uses the
    // publish directory instead, and a test that guessed one would fail with "no email" for a flow
    // that worked perfectly.
    private static string[] MailPickupDirectories()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return
        [
            Path.Combine(dir.FullName, "samples", "Rask.Example.Auth", "mail-pickup"),
            Path.Combine(dir.FullName, "mail-pickup"),
        ];
    }

    private static HashSet<string> ExistingMail(string[] pickups) =>
        [.. pickups.Where(Directory.Exists).SelectMany(p => Directory.GetFiles(p, "*.eml"))];

    /// <summary>Waits for a NEW message carrying a reset link, and returns the link.</summary>
    /// <remarks>
    ///     Only files that were not there before this journey started count. The two journeys in this
    ///     collection share a pickup directory, and picking up a message from an earlier run would
    ///     hand back a token that has already been spent.
    /// </remarks>
    private static async Task<string> WaitForResetLinkAsync(string[] pickups, HashSet<string> before)
    {
        // Generous, because two waits stack: the queue is polled on an interval, and the file lands
        // after that. This is a bound on a real failure, not a guess at the happy path's duration.
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            foreach (var pickup in pickups.Where(Directory.Exists))
            {
                foreach (var file in Directory.GetFiles(pickup, "*.eml"))
                {
                    if (before.Contains(file))
                    {
                        continue;
                    }

                    // Read while the message may still be being flushed, so a partial read is normal
                    // rather than a failure — the next pass sees the rest.
                    string body;

                    try
                    {
                        body = await File.ReadAllTextAsync(file);
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    if (ResetLink().Match(Decode(body)) is { Success: true } match)
                    {
                        return match.Value;
                    }
                }
            }

            await Task.Delay(500);
        }

        Assert.Fail(
            $"No reset email appeared in [{string.Join(", ", pickups)}] within 60s. The mail battery "
            + "writes one .eml per message when no SMTP host is configured; an empty directory means "
            + "nothing was queued.");
        return "";
    }

    /// <summary>Turns the stored message back into the URL a mail client would follow.</summary>
    /// <remarks>
    ///     <para>
    ///         HTML entities always come off: the link lives in an <c>href</c>, so its query separator
    ///         is written <c>&amp;amp;</c>, and leaving it loses every parameter after the first.
    ///     </para>
    ///     <para>
    ///         <b>Quoted-printable comes off only when the message says it applied it.</b> Decoding it
    ///         unconditionally is actively destructive here, and quietly: <c>=XX</c> is the escape, so
    ///         a URL carrying <c>?userId=8c48…</c> has its <c>=8c</c> eaten and arrives as
    ///         <c>?userId486d…</c> — a link that looks plausible, parses to no parameters at all, and
    ///         lands on "that link is incomplete" as though the framework had built it wrong.
    ///     </para>
    /// </remarks>
    private static string Decode(string raw)
    {
        var decoded = raw;

        if (raw.Contains("Content-Transfer-Encoding: quoted-printable", StringComparison.OrdinalIgnoreCase))
        {
            // Soft line breaks first — they are what splits a long URL across lines.
            var unfolded = decoded
                .Replace("=\r\n", "", StringComparison.Ordinal)
                .Replace("=\n", "", StringComparison.Ordinal);

            decoded = Regex.Replace(
                unfolded,
                "=([0-9A-Fa-f]{2})",
                m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        }

        // The token's own percent-encoding is left alone: that belongs in the URL, and undoing it is
        // exactly the bug the unit suite pins.
        return System.Net.WebUtility.HtmlDecode(decoded);
    }

    [GeneratedRegex(@"https?://[^""\s]+/reset-password\?[^""\s]+")]
    private static partial Regex ResetLink();
}
