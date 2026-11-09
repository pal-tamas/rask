using System.Globalization;
using Microsoft.Playwright;
using Rask.Wire;

namespace Rask.Testing;

/// <summary>
///     Signs a page in to an app on Rask.Auth, registering the account first when there is none: through Rask.Auth's own
///     API (<c>/api/auth/login</c>, <c>/api/auth/register</c>) where the app maps it — a WebAssembly app's host — and
///     otherwise through the sign-in and register pages <c>rask new</c> writes (<c>/login</c>, <c>/register</c>).
/// </summary>
/// <remarks>
///     Either way it is the browser that signs in, so it keeps the cookie exactly as it does for a person. The first
///     account an app registers is its administrator and needs the app's one-time token: the site starts the app with a
///     token of its own (<c>Rask__Auth__FirstRunToken</c>) and sends it with every registration, where it counts only while
///     the app is unclaimed.
/// </remarks>
internal static class RaskAuthSignIn
{
    private static readonly TimeSpan Arrival = TimeSpan.FromSeconds(30);

    // Same-origin fetch from the page: the endpoints ask for their header rather than a form round-trip.
    private const string Post = """
        async ([path, body, header]) => {
            const answer = await fetch(path, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', [header]: '1' },
                body: JSON.stringify(body),
            });
            return answer.status + ' ' + await answer.text();
        }
        """;

    /// <summary>Signs <paramref name="page" /> in as <paramref name="name" />, registering the account first when there is none.</summary>
    /// <param name="page">A page of the site's, still blank.</param>
    /// <param name="landing">A path on the app to fetch from: its health check, which renders nothing.</param>
    /// <param name="name">The account's name: an email address on an app that signs in by email.</param>
    /// <param name="password">Its password.</param>
    /// <param name="firstRunToken">The token the app was started with.</param>
    /// <exception cref="InvalidOperationException">Neither signing in nor registering worked; the message says what the app answered.</exception>
    public static async Task SignIn(IPage page, string landing, string name, string password, string firstRunToken)
    {
        await page.GotoAsync(landing).ConfigureAwait(false);
        var login = await Send(page, AuthApi.Login, new(StringComparer.Ordinal) { ["email"] = name, ["password"] = password })
            .ConfigureAwait(false);
        if (StatusOf(login) == 404)
        {
            await ThroughPages(page, name, password, firstRunToken).ConfigureAwait(false);
            return;
        }

        if (IsSuccess(login))
        {
            return;
        }

        var register = await Send(page, AuthApi.Register, new(StringComparer.Ordinal)
        {
            ["email"] = name,
            ["password"] = password,
            ["firstRunToken"] = firstRunToken,
        }).ConfigureAwait(false);
        if (IsSuccess(register))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Signing in as {name} failed: {AuthApi.DefaultPrefix}{AuthApi.Login} answered {login}, and "
            + $"{AuthApi.DefaultPrefix}{AuthApi.Register} answered {register}. {OwnSignIn}");
    }

    private const string OwnSignIn =
        "An app that signs in its own way says how: site.SignIn = async (page, name) => { … };";

    // The pages `rask new` writes, by the ids it gives their fields. An app that changed them signs in its own way.
    private static async Task ThroughPages(IPage page, string name, string password, string firstRunToken)
    {
        await page.GotoAsync("/login").ConfigureAwait(false);
        await page.WaitForRask().ConfigureAwait(false);
        await page.Locator("#email").FillAsync(name).ConfigureAwait(false);
        await page.Locator("#password").FillAsync(password).ConfigureAwait(false);
        if (await Submitted(page, "#login-submit", "/login", "#login-error").ConfigureAwait(false))
        {
            return;
        }

        await page.GotoAsync("/register").ConfigureAwait(false);
        await page.WaitForRask().ConfigureAwait(false);
        await page.Locator("#email").FillAsync(name).ConfigureAwait(false);
        await page.Locator("#password").FillAsync(password).ConfigureAwait(false);
        var token = page.Locator("#first-run-token");
        if (await token.CountAsync().ConfigureAwait(false) > 0)
        {
            await token.FillAsync(firstRunToken).ConfigureAwait(false);
        }

        if (await Submitted(page, "#register-submit", "/register", "#register-error").ConfigureAwait(false))
        {
            return;
        }

        var said = await page.Locator("body").InnerTextAsync().ConfigureAwait(false);
        throw new InvalidOperationException(
            $"Signing in as {name} failed: neither /login nor /register let the account in. {OwnSignIn} "
            + $"/register said: {said.ReplaceLineEndings(" ").Trim()}");
    }

    // Submits the form and waits for its answer: the page going on from the form — a sign-in lands where it was going —
    // or the form saying why not. The cookie is written by a request of its own as the page goes on, so a sign-in is
    // over once a cookie the page did not have before is there.
    private static async Task<bool> Submitted(IPage page, string submit, string form, string error)
    {
        var before = (await page.Context.CookiesAsync().ConfigureAwait(false)).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        await page.Locator(submit).ClickAsync().ConfigureAwait(false);
        var answer = await page.WaitForFunctionAsync(
            "([form, error]) => !location.pathname.startsWith(form) || document.querySelector(error) !== null",
            new[] { form, error },
            new PageWaitForFunctionOptions { Timeout = (float)Arrival.TotalMilliseconds }).ConfigureAwait(false);
        await answer.DisposeAsync().ConfigureAwait(false);
        if (new Uri(page.Url).AbsolutePath.StartsWith(form, StringComparison.Ordinal))
        {
            return false;
        }

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.Elapsed < Arrival
               && (await page.Context.CookiesAsync().ConfigureAwait(false)).All(c => before.Contains(c.Name)))
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        return true;
    }

    private static Task<string> Send(IPage page, string endpoint, Dictionary<string, object?> body) =>
        page.EvaluateAsync<string>(Post, new object[] { AuthApi.DefaultPrefix + endpoint, body, AuthApi.RequestHeader });

    private static int StatusOf(string answer) =>
        int.TryParse(answer.AsSpan(0, Math.Max(0, answer.IndexOf(' ', StringComparison.Ordinal))), NumberStyles.None,
            CultureInfo.InvariantCulture, out var status)
            ? status
            : 0;

    private static bool IsSuccess(string answer) => StatusOf(answer) is >= 200 and < 300;
}
