using System.Security.Claims;
using Microsoft.JSInterop;
using Rask.Core.Authentication;
using Rask.Core.Browser;

namespace Rask.Wasm.Authentication;

/// <summary>
///     Whether the browser's credential still names the user this tab renders for — the browser host's half of
///     <c>rask-auth-watch.ts</c>.
/// </summary>
/// <remarks>
///     <para>
///         A browser app reads its user once, at boot, and then trusts it: sign out in another tab, let the cookie
///         lapse, or take a right away, and the tab goes on drawing the page for whoever it booted with. Every request
///         it makes carries the cookie as it is now, so the server is never fooled — but the page is. So the tab asks
///         again, at the same moments a Server tab does (as it becomes the reader's, and before the first event after
///         a pause), and whenever Rask's own API refuses it with a 401 (<see cref="SignInRefusals" />).
///     </para>
///     <para>
///         Asking is the user provider's <see cref="IUserProvider.Refresh" />. Its answer is compared with the user
///         the tab had: the same user with the same rights changes nothing, another user is simply taken (the page
///         renders for them, and its route guard decides what they may see), and nobody where there was somebody is
///         a sign-in that ended — the page goes to the sign-in page with the way back, whatever page it is, with no
///         message, as a Server tab does. A provider that could not answer — offline, a server error — keeps the user
///         it had: no answer is not "changed".
///     </para>
/// </remarks>
internal sealed class WasmUserWatch(IUserProvider users, IJSRuntime js)
{
    // The answers rask.wasm.js reads (rask-wasm-auth.ts): rask-auth-watch.ts's WhoAnswer, or `off` when there is no
    // user to watch. "Not the same" covers another user and nobody alike: either way what the reader did meanwhile was
    // done for the user before, and is dropped.
    internal const string SameSomebody = """{"same":true,"user":true}""";
    internal const string SameNobody = """{"same":true,"user":false}""";
    internal const string Other = """{"same":false}""";
    internal const string Off = """{"off":true}""";
    internal const string Unanswered = "";

    // The question that is out, so a refusal and a returning reader asking at once ask the provider once.
    private Task<string>? _asking;

    /// <summary>Raised when the tab's sign-in ended: it had a user, and the browser carries nobody now.</summary>
    internal event EventHandler? SignInEnded;

    /// <summary>
    ///     Set when the sign-in ended, until the page has been sent to the sign-in page (WasmLiveSession reads and
    ///     clears it, as a Server session reads its own).
    /// </summary>
    internal bool HasEnded { get; set; }

    /// <summary>
    ///     False when the app knows no user to watch: the browser host's anonymous default, which nothing signs in.
    /// </summary>
    internal bool Watches => users is not AnonymousUserProvider;

    /// <summary>Asks who the browser carries now, and answers as <c>rask-wasm-auth.ts</c> reads it.</summary>
    internal Task<string> Ask()
    {
        if (!Watches)
        {
            return Task.FromResult(Off);
        }

        if (_asking is { IsCompleted: false } asking)
        {
            return asking;
        }

        return _asking = AskOnce();
    }

    /// <summary>
    ///     Rask's own API refused a request with a 401. A tab that had a user asks who the browser carries now; one
    ///     that had none has nothing to lose, and the caller reads the refusal as it always did.
    /// </summary>
    /// <remarks>
    ///     The question already out is not waited for: it may be the very request that was refused — a provider
    ///     whose own endpoint answers 401 for nobody — and its answer comes regardless.
    /// </remarks>
    internal Task Refused()
    {
        if (!Watches || _asking is { IsCompleted: false } || !PrincipalRights.IsSignedIn(users.Current))
        {
            return Task.CompletedTask;
        }

        return Ask();
    }

    private async Task<string> AskOnce()
    {
        var held = users.Current;
        try
        {
            await users.Refresh().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JSException)
        {
            return Unanswered;
        }

        return await AnswerAbout(held, users.Current).ConfigureAwait(false);
    }

    private async Task<string> AnswerAbout(ClaimsPrincipal held, ClaimsPrincipal now)
    {
        if (PrincipalRights.Same(held, now))
        {
            return PrincipalRights.IsSignedIn(now) ? SameSomebody : SameNobody;
        }

        if (PrincipalRights.IsSignedIn(now) || !PrincipalRights.IsSignedIn(held))
        {
            return Other;
        }

        // Marked before anything is awaited: the provider's Changed has asked for a render, and that render must
        // find the page on its way to the sign-in page, not draw it for nobody first.
        HasEnded = true;
        SignInEnded?.Invoke(this, EventArgs.Empty);

        // What the service worker kept, it kept for the user who has gone — as a sign-out here empties it.
        await OfflineCache.Clear(js).ConfigureAwait(false);
        return Other;
    }
}
