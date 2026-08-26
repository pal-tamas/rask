namespace Rask.Server.Http;

/// <summary>The headers a shell response should carry.</summary>
internal readonly record struct ShellCacheDecision(string CacheControl, string? Pragma, string? Vary);

/// <summary>
///     Decides how a shell response may be cached.
/// </summary>
/// <remarks>
///     A pure function of four facts, deliberately: the whole matrix is then unit-testable with no
///     host, no client and no timing, which matters because the failure mode here is a disclosure
///     bug that no ordinary test would notice — a page cached under the wrong principal looks
///     perfectly correct to the person who requested it.
/// </remarks>
internal static class ShellCachePolicy
{
    // The shell embeds the session id, which is the de-facto bearer for the WS / upload / download
    // endpoints. Any response carrying one must never be stored by a shared proxy, a bfcache, or
    // history.
    private const string NoStore = "no-store, no-cache, must-revalidate, private";

    internal static ShellCacheDecision For(bool interactive, bool authenticated, bool faulted, int statusCode)
    {
        if (interactive)
        {
            // Unchanged from before static pages existed: this response carries a session id.
            return new ShellCacheDecision(NoStore, "no-cache", null);
        }

        if (faulted || statusCode >= 400)
        {
            // A transient outage cached as the app's homepage is an outage amplifier — the error
            // outlives the fault that caused it, and no deploy clears it.
            return new ShellCacheDecision(NoStore, "no-cache", null);
        }

        if (authenticated)
        {
            // No session id in the body, but the body itself is this user's. Nothing shared may
            // hold it.
            return new ShellCacheDecision(NoStore, "no-cache", null);
        }

        // Anonymous and static: the response is the same for everyone who is nobody. `private`
        // keeps every shared cache out, while dropping `no-store` is what restores bfcache and
        // instant back/forward — the user-visible win. `max-age=0, must-revalidate` keeps the
        // browser asking, so content stays fresh and a future ETag can turn this into a 304.
        //
        // `Vary: Cookie` because "anonymous" is itself a function of the cookie: a cache that
        // stored this without varying on it could serve the logged-out page to a signed-in user.
        return new ShellCacheDecision("private, max-age=0, must-revalidate", null, "Cookie");
    }
}
