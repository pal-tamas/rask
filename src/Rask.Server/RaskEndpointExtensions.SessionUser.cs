using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Server;

/// <content>Who a live session acts for, asked again at every event.</content>
public static partial class RaskEndpointExtensions
{
    /// <summary>How long a render nobody asked for trusts the last answer about the session's user.</summary>
    /// <remarks>
    ///     An event — a handler, a navigation, an attach — always asks. A render the server starts itself (a timer,
    ///     a followed service, a data change) asks too, unless something asked within this long: a page that
    ///     repaints twenty times a second would otherwise read the user's row twenty times a second.
    /// </remarks>
    internal static readonly TimeSpan PushedRenderTrusts = TimeSpan.FromSeconds(1);

    private const string RevalidatorDoc = "docs/authentication.md#what-ties-a-live-page-to-its-user";

    /// <summary>
    ///     Asks who the session's user is now, before an event runs for them: the revalidator about the principal,
    ///     then the app's tenant resolver. False when the answer could not be had, and the event must not run.
    /// </summary>
    /// <remarks>
    ///     With no revalidator, or the same principal handed back, this allocates nothing.
    /// </remarks>
    internal static ValueTask<bool> TryAskWhoAsync(
        LiveSession session, string what, CancellationToken ct, bool tenantToo = true)
    {
        try
        {
            // An answer that is already there is taken without a state machine: it is the usual one, and this
            // runs before every event.
            var asked = RevalidateUserAsync(session, ct);
            if (!asked.IsCompletedSuccessfully)
            {
                return AnsweredLaterAsync(asked, session, what, tenantToo);
            }

            Asked(session, tenantToo);
            return new ValueTask<bool>(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ValueTask<bool>(Refused(what, ex));
        }
    }

    private static async ValueTask<bool> AnsweredLaterAsync(ValueTask asked, LiveSession session, string what, bool tenantToo)
    {
        try
        {
            await asked.ConfigureAwait(false);
            Asked(session, tenantToo);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Refused(what, ex);
        }
    }

    // Noted whether or not there was a revalidator to ask: it is when the session last made sure, which is what a
    // render nobody asked for and the later events of a long batch go by.
    private static void Asked(LiveSession session, bool tenantToo)
    {
        session.NoteUserChecked();
        if (tenantToo)
        {
            session.AskTenantAgain();
        }
    }

    // Closed, not open: an answer that could not be had is not "yes". The reader's next action asks again.
    private static bool Refused(string what, Exception ex)
    {
        RaskDiagnostics.Report(
            RaskLogLevel.Error, "Rask.Live",
            $"Rask Live: could not ask who the session's user is now, so {what} was refused", ex);
        return false;
    }

    /// <summary>Asks the app's <see cref="ISessionRevalidator" /> about the session's principal, and takes its answer.</summary>
    /// <remarks>
    ///     <see langword="null" /> ends the sign-in: the principal is cleared and the page is owed the sign-in page.
    ///     A principal with other rights replaces the one held and the whole page renders again, mounting nothing.
    ///     Nothing is asked for a session nobody is signed in to, or when the app registered no revalidator.
    /// </remarks>
    private static ValueTask RevalidateUserAsync(LiveSession session, CancellationToken ct)
    {
        var users = session.Users;
        if (!users.IsSignedIn || session.Revalidator is not { } revalidator)
        {
            return default;
        }

        var held = users.Current;
        var asked = Ask(revalidator, held, ct);
        if (!asked.IsCompletedSuccessfully)
        {
            return TakeLaterAsync(asked, session, held);
        }

        TakeAnswer(session, held, asked.Result);
        return default;
    }

    // The revalidator's answer, a cancellation it disguised made one again (CancelledCallback): an attach or an event
    // whose client went away ends quietly, and only a real failure is refused and reported.
    private static ValueTask<ClaimsPrincipal?> Ask(ISessionRevalidator revalidator, ClaimsPrincipal user, CancellationToken ct)
    {
        try
        {
            var asked = revalidator.Revalidate(user, ct);
            return asked.IsCompletedSuccessfully ? asked : AskedLaterAsync(asked, ct);
        }
        catch (Exception ex) when (CancelledCallback.IsDisguised(ex, ct))
        {
            throw CancelledCallback.AsCancellation(ex, ct);
        }
    }

    private static async ValueTask<ClaimsPrincipal?> AskedLaterAsync(ValueTask<ClaimsPrincipal?> asked, CancellationToken ct)
    {
        try
        {
            return await asked.ConfigureAwait(false);
        }
        catch (Exception ex) when (CancelledCallback.IsDisguised(ex, ct))
        {
            throw CancelledCallback.AsCancellation(ex, ct);
        }
    }

    private static async ValueTask TakeLaterAsync(ValueTask<ClaimsPrincipal?> asked, LiveSession session, ClaimsPrincipal held) =>
        TakeAnswer(session, held, await asked.ConfigureAwait(false));

    private static void TakeAnswer(LiveSession session, ClaimsPrincipal held, ClaimsPrincipal? fresh)
    {
        // An answer about a principal the session no longer holds is about somebody it no longer acts for: a
        // connection attached meanwhile, or another answer came first. It changes nothing.
        var users = session.Users;
        if (!ReferenceEquals(users.Current, held) || ReferenceEquals(fresh, held))
        {
            return;
        }

        var answer = AnswerAbout(held, fresh);
        if (answer is null)
        {
            users.Clear();
            session.SignInEnded = true;
        }
        else if (PrincipalRights.Same(held, answer))
        {
            // The same user with the same rights, issued again: kept, so what reads the token's own claims — when
            // it lapses — reads the new one's. Nothing is told, because nothing changed.
            users.Keep(answer);
            return;
        }
        else
        {
            users.Set(answer);
        }

        RenderForOtherRights(session);

        // Their tenant may be another too, and what follows — a render nobody asked for among it — works in it.
        session.AskTenantAgain();
    }

    // What a revalidator's answer about `asked` comes to: the principal to act under, or null when their sign-in has
    // ended. An answer that names somebody else is not an answer about them, and ends it too.
    private static ClaimsPrincipal? AnswerAbout(ClaimsPrincipal asked, ClaimsPrincipal? fresh)
    {
        if (fresh is null || !PrincipalRights.IsSignedIn(fresh))
        {
            return null;
        }

        if (UserKey(asked) is not { } subject || string.Equals(subject, UserKey(fresh), StringComparison.Ordinal))
        {
            return fresh;
        }

        if (Interlocked.Exchange(ref _otherSubjectReported, 1) == 0)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Warning, "Rask.Live",
                "Rask Live: the ISessionRevalidator answered a question about one user with a principal for another "
                + "(a different NameIdentifier, or Name). A live session never changes hands that way: the session was "
                + "signed out. Return the principal of the user it was asked about, or null.");
        }

        return null;
    }

    private static int _otherSubjectReported;

    // Everything on the page may have read the principal as it rendered, so everything renders again. Nothing is
    // mounted: what a component holds — a field half typed, a list it loaded — stays as it is.
    private static void RenderForOtherRights(LiveSession session)
    {
        Component.MarkSubtreeDirtyInternal(session.View);
        session.ForgetReads();
    }

    /// <summary>
    ///     Makes the session act for <paramref name="user" /> — the principal of the connection that just attached,
    ///     or of the request that just posted — as the sign-in behind it stands now.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A cookie or a token can outlive its sign-in, so the revalidator is asked FIRST, about the principal
    ///         the connection carries, and the session is given only its answer: nothing that follows the session's
    ///         user ever sees what the cookie claims. Throws when it cannot be asked, having adopted nothing: the
    ///         caller ends the connection, and the client's reconnect asks again.
    ///     </para>
    ///     <para>
    ///         Never in the middle of a dispatch: a handler that read as one user and tenant writes as the same.
    ///         With nothing of the session running the answer is put in place here. Otherwise it is held and adopted
    ///         on the handler chain, when what runs has finished — and never waited for: the handler may itself be
    ///         waiting for this very connection, for the answer to a call into the page's script.
    ///     </para>
    /// </remarks>
    /// <returns>False when the answer was held for later, and nothing may be rendered for it yet.</returns>
    private static async Task<bool> AdoptUserAsync(LiveSession session, ClaimsPrincipal user, bool attached, CancellationToken ct)
    {
        var answer = await AnswerForAsync(session, user, ct).ConfigureAwait(false);

        if (await session.Lock.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            try
            {
                // One that arrived earlier and still waits is older than this: the latest wins.
                session.TakeArrived();
                Adopt(session, user, answer);
            }
            finally
            {
                session.Lock.Release();
            }

            return true;
        }

        session.HoldArrived(new ArrivedUser(user, answer, session.Users.Current));
        session.EnqueueOnHandlerChain(previous => AdoptBehindAsync(previous, session, attached, ct));
        return false;
    }

    // What the revalidator makes of the principal a connection carries: the one to act under, or null when its
    // sign-in has ended. Asked about nobody, and by an app with no revalidator, it is the principal itself.
    private static async ValueTask<ClaimsPrincipal?> AnswerForAsync(LiveSession session, ClaimsPrincipal user, CancellationToken ct) =>
        PrincipalRights.IsSignedIn(user) && session.Revalidator is { } revalidator
            ? AnswerAbout(user, await Ask(revalidator, user, ct).ConfigureAwait(false))
            : user;

    // The held credential's link on the handler chain: behind the dispatch that was running as it arrived, and
    // ahead of every event read after it.
    private static async Task AdoptBehindAsync(Task previous, LiveSession session, bool attached, CancellationToken ct)
    {
        // Never inline: whoever queued this holds the chain's gate, and must not be made to wait for a render.
        await Task.Yield();
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        try
        {
            if (!await AdoptArrivedAsync(session).ConfigureAwait(false))
            {
                return;
            }

            // Only now is there anything to show the new principal: what the attach would have rendered at once.
            await (attached ? RenderAttachedAsync(session, ct) : session.FlushPendingRenderAsync()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The connection closed, or the session went, while this waited its turn.
        }
        catch (Exception ex)
        {
            RaskDiagnostics.Report(
                RaskLogLevel.Error, "Rask.Live",
                "Rask Live: could not render for the user a connection brought while a handler ran", ex);
        }
    }

    private static async Task<bool> AdoptArrivedAsync(LiveSession session)
    {
        await session.Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Taken by a later arrival that found the session idle, or dropped: it was let in against the user the
            // session held then, and the session holds somebody else now.
            if (session.TakeArrived() is not { } arrived || !StillAdmitted(session, arrived))
            {
                return false;
            }

            Adopt(session, arrived.Carried, arrived.Answer);
            return true;
        }
        finally
        {
            session.Lock.Release();
        }
    }

    // While the session holds whom it held as the credential arrived, the check made then stands — a sign-out's
    // handoff, which no user check admits, among them. Otherwise it is made again, against whom it holds now.
    private static bool StillAdmitted(LiveSession session, ArrivedUser arrived)
    {
        var holds = session.Users.Current;
        return ReferenceEquals(holds, arrived.Held)
               || PrincipalRights.Same(holds, arrived.Held)
               || MayDrive(arrived.Carried, session);
    }

    // Puts a revalidated answer in place, in one step with nothing awaited: the principal, what the connection
    // carried, and the tenant that follows from them.
    private static void Adopt(LiveSession session, ClaimsPrincipal carried, ClaimsPrincipal? answer)
    {
        var users = session.Users;
        var before = users.Current;
        session.ConnectionUser = carried;
        session.NoteUserChecked();

        var changed = answer is null ? users.IsSignedIn : !PrincipalRights.Same(before, answer);
        if (answer is null)
        {
            users.Clear();
            session.SignInEnded = true;
        }
        else if (changed)
        {
            users.Set(answer);
        }
        else
        {
            users.Keep(answer);
        }

        if (changed)
        {
            RenderForOtherRights(session);

            // An event renders when it ends. A connection that attached with other rights and sends nothing yet
            // is owed the render that shows them, behind whatever the attach itself has to send first.
            session.OweAttachedRender();
        }

        session.AskTenantAgain();
    }

    // The principal the session's connection last brought: one still waiting to be adopted, or the adopted one.
    private static ClaimsPrincipal? CarriedBy(LiveSession session) => session.ArrivedUser?.Carried ?? session.ConnectionUser;

    /// <summary>
    ///     Adopts the principal an HTTP post carries when it is not the one the session's stream opened with: a post
    ///     brings the browser's credential as it is now, which a stream opened earlier never sees.
    /// </summary>
    private static Task AdoptPostedUserAsync(LiveSession session, ClaimsPrincipal user, CancellationToken ct)
    {
        if (CarriedBy(session) is not { } carried || !PrincipalRights.Same(carried, user))
        {
            return AdoptUserAsync(session, user, attached: false, ct);
        }

        // One that still waits to be adopted is not the session's to keep a token for yet.
        if (session.ConnectionUser is { } attached && session.ArrivedUser is null)
        {
            KeepToken(session, attached, user);
        }

        return Task.CompletedTask;
    }

    // The browser's credential was issued again and says what it said: the session holds the new one from here on,
    // so a revalidator that reads when the token lapses reads the new date. Only when the session still holds the
    // connection's own principal — one a revalidator put in its place is the revalidator's to replace.
    private static void KeepToken(LiveSession session, ClaimsPrincipal attached, ClaimsPrincipal issuedAgain)
    {
        if (ReferenceEquals(attached, issuedAgain))
        {
            return;
        }

        session.ConnectionUser = issuedAgain;
        if (ReferenceEquals(session.Users.Current, attached))
        {
            session.Users.Keep(issuedAgain);
        }
    }

    /// <summary>
    ///     Whether a render the server started itself may go out: asks about the user when nothing has within
    ///     <see cref="PushedRenderTrusts" />, and sends the page to the sign-in page instead when their sign-in ended.
    /// </summary>
    /// <remarks>
    ///     Asked as nobody's action (<see cref="RevalidationCause" />), so a page that repaints by itself does not
    ///     keep a sliding sign-in alive. The tenant resolver is asked only when the answer changed the principal:
    ///     it reads a request, and here there is none.
    /// </remarks>
    internal static async ValueTask<bool> MayPushAsync(LiveSession session)
    {
        if (!session.Users.IsSignedIn && !session.SignInEnded)
        {
            return true;
        }

        if (session.Revalidator is not null && AskedLongAgo(session) && !await AskUnattendedAsync(session, "a render").ConfigureAwait(false))
        {
            return false;
        }

        if (!session.SignInEnded)
        {
            return true;
        }

        using var navigating = session.Services.GetRequiredService<Navigator>().EnterHandler();
        await EnforceAuthAndRenderAsync(session, null, false).ConfigureAwait(false);
        return false;
    }

    private static bool AskedLongAgo(LiveSession session) => !session.UserCheckedWithin(PushedRenderTrusts);

    private static async ValueTask<bool> AskUnattendedAsync(LiveSession session, string what)
    {
        using var nobodys = RevalidationCause.EnterUnattended();
        return await TryAskWhoAsync(session, what, CancellationToken.None, tenantToo: false).ConfigureAwait(false);
    }

    /// <summary>
    ///     Whether a frame that acts without an event — a script's call into the server — may act: not for a session
    ///     whose sign-in ended, and only after asking when nothing has within <see cref="PushedRenderTrusts" />.
    /// </summary>
    /// <remarks>
    ///     Such a frame is answered on the reader, beside the handler it may be serving, so it cannot wait its turn
    ///     behind one. It asks where it is, bounded as a pushed render is, and is dropped when no answer can be had.
    /// </remarks>
    private static ValueTask<bool> MayActInlineAsync(LiveSession session)
    {
        if (session.SignInEnded)
        {
            return new ValueTask<bool>(false);
        }

        return !session.Users.IsSignedIn || session.Revalidator is null || !AskedLongAgo(session)
            ? new ValueTask<bool>(true)
            : AskedInlineAsync(session);
    }

    private static async ValueTask<bool> AskedInlineAsync(LiveSession session) =>
        await TryAskWhoAsync(session, "a script's call", CancellationToken.None, tenantToo: false).ConfigureAwait(false)
        && !session.SignInEnded;

    // Where a session whose sign-in ended goes instead of the page it is on, guarded or not: the sign-in page,
    // with the way back. Null once it is there, or when there is none to go to — the page's own guard then decides.
    //
    // The app's own sign-in page is reached in place, and another application's on this mapping is loaded as a page.
    // Without one, the sign-in page the host's authentication names (HostSignInPage) is loaded as a page, with the
    // page's full address as the way back, and the session stays one whose sign-in ended until the browser has gone.
    private static async ValueTask<GuardRedirect?> SignInAgainAsync(
        LiveSession session, string path, Rask.Core.Routing.IQueryCollection query)
    {
        var pageUrl = Rask.Core.Routing.QueryString.Build(path, query);
        var signInRoutes = session.SignInRoutes?.Invoke() ?? session.Services.GetRequiredService<RouteState>().CurrentTable;
        if (!HasSignInPage(signInRoutes))
        {
            if (await HostSignInPage.FindAsync(session.Services).ConfigureAwait(false) is { } host)
            {
                return new GuardRedirect(host.For(LiveOptions.PathBase + pageUrl), string.Empty, AsPage: true, Outside: true);
            }

            session.SignInEnded = false;
            return null;
        }

        session.SignInEnded = false;
        if (string.Equals(path, RouteAuthorizationGuard.ChallengePath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var anotherApplications = session.OwnsPath is { } owns && !owns(RouteAuthorizationGuard.ChallengePath);
        return new GuardRedirect(
            RouteAuthorizationGuard.ChallengePath, "?returnUrl=" + Uri.EscapeDataString(pageUrl), AsPage: anotherApplications);
    }

    // Said once, as the app's live endpoints are mapped: with sign-in and nothing to ask, an open page keeps whatever its connection
    // carried for as long as that connection lasts.
    private static void WarnWhenNothingRevalidates(IServiceProvider services)
    {
        // Asked of the container, not resolved: a revalidator may need a database that is not there to be had yet.
        if (services.GetService<IServiceProviderIsService>()?.IsService(typeof(ISessionRevalidator)) != false
            || services.GetService<IAuthenticationSchemeProvider>() is not { } schemes
            || !HasSchemes(schemes))
        {
            return;
        }

        RaskDiagnostics.Report(
            RaskLogLevel.Warning, "Rask.Live",
            "Rask Live: this app signs users in and registers no ISessionRevalidator, so a live page cannot ask "
            + "whether its user is still signed in: a sign-out elsewhere, an expired token or a removed right "
            + "reaches an open page only when its connection is opened again. Register one — "
            + "services.AddSingleton<ISessionRevalidator, …>() — see " + RevalidatorDoc + ".");
    }

    // The provider answers from memory. One that would have to wait is taken to have schemes.
    private static bool HasSchemes(IAuthenticationSchemeProvider schemes)
    {
        var all = schemes.GetAllSchemesAsync();
        return !all.IsCompletedSuccessfully || all.Result.Any();
    }

    // The session rides the Rask-Session header; a runtime from before named it in the path, and still reaches it.
    private const string WhoPath = "/_rask/auth/who";

    // How often one session's tab may ask. It asks as it comes back to the reader and before an event after a
    // pause: a handful of times a minute.
    private const int WhoQuestionsPerSecond = 4;

    private static readonly byte[] WhoSameSomebody = "{\"same\":true,\"user\":true}"u8.ToArray();
    private static readonly byte[] WhoSameNobody = "{\"same\":true,\"user\":false}"u8.ToArray();
    private static readonly byte[] WhoLater = "{\"later\":true}"u8.ToArray();
    private static readonly byte[] WhoOther = "{\"same\":false}"u8.ToArray();

    /// <summary>
    ///     Answers a tab asking whether the browser's credential still names the user its connection opened with.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A socket sees the browser's cookie once, at its upgrade. When the cookie changes afterwards — signed
    ///         in, out, or issued again with another tenant, by this app or by another one on the host — an open tab
    ///         has no way to know. This request is authenticated like any other, so it carries the cookie as it is
    ///         now; the tab asks as it becomes the reader's again, and opens its connection anew when the answer is
    ///         anything but "the same". The attach then adopts the principal, or refuses it, as it always did.
    ///     </para>
    ///     <para>
    ///         It says nothing else: no claim, no name. A session that does not exist and one that is not the
    ///         caller's get the same answer, so an id tells a stranger nothing.
    ///     </para>
    /// </remarks>
    private static Task WhoAsync(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        if (!IsSameOrigin(ctx.Request) || IsCrossSiteFetch(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        var sessionId = NamedSession(ctx);
        var session = sessionId is null ? null : ctx.RequestServices.GetRequiredService<LiveSessionStore>().Peek(sessionId);

        ctx.Response.ContentType = "application/json";
        return ctx.Response.Body.WriteAsync(WhoAnswerFor(session, UserOf(ctx)), ctx.RequestAborted).AsTask();
    }

    private static byte[] WhoAnswerFor(LiveSession? session, ClaimsPrincipal request)
    {
        // Counted only for a caller the session answers to: a stranger with the id cannot use up its user's
        // questions, and gets one answer whatever the id.
        if (session is null || !MayDrive(request, session))
        {
            return WhoOther;
        }

        // Only a caller the session answers to is ever told to ask later, so the answer tells a stranger nothing:
        // they were answered above. The tab holds what it has to send and asks again.
        if (!session.AdmitWhoQuestion(WhoQuestionsPerSecond))
        {
            return WhoLater;
        }

        // What the connection carried, not what a revalidator has made of it since: the question is whether the
        // browser would bring another credential if the connection were opened again.
        var attached = CarriedBy(session) ?? session.Users.Current;
        if (!PrincipalRights.Same(attached, request))
        {
            return WhoOther;
        }

        if (session.ConnectionUser is { } carried && session.ArrivedUser is null)
        {
            KeepToken(session, carried, request);
        }

        return PrincipalRights.IsSignedIn(request) ? WhoSameSomebody : WhoSameNobody;
    }

    /// <summary>The cookie a tab sets before it reloads because its connection was refused after it had a user.</summary>
    internal const string EndedCookie = "rask-ended";

    /// <summary>
    ///     Its name over https: the <c>__Host-</c> prefix makes the browser refuse it from anything but this very
    ///     host — a sibling subdomain can neither set it nor shadow it — and only with <c>Secure</c> and
    ///     <c>Path=/</c>.
    /// </summary>
    internal const string EndedCookieOverHttps = "__Host-" + EndedCookie;

    /// <summary>
    ///     Sends a tab that had a user and has none now to the sign-in page, with the way back — the page's own
    ///     request being the first place the server sees that the browser no longer carries anybody.
    /// </summary>
    /// <remarks>
    ///     The tab says it had one, by a cookie that lasts seconds and names the app it was written for: Rask
    ///     cannot tell from an anonymous request. A visitor who never signed in never sets it, another app on the
    ///     host ignores it, and a public page goes on serving them. All it can do is redirect its own bearer, once.
    /// </remarks>
    private static async ValueTask<bool> SentToSignInAsync(
        HttpContext ctx, string path, ClaimsPrincipal user, string pathBase, RaskRootSelector selector)
    {
        // Over https only the prefixed name is believed: the plain one can be planted from next door.
        var name = ctx.Request.IsHttps ? EndedCookieOverHttps : EndedCookie;
        if (!ctx.Request.Cookies.TryGetValue(name, out var writtenFor)
            || !string.Equals(Uri.UnescapeDataString(writtenFor ?? string.Empty), pathBase.Length == 0 ? "/" : pathBase, StringComparison.Ordinal))
        {
            return false;
        }

        ForgetEnded(ctx, name);

        if (PrincipalRights.IsSignedIn(user)
            || string.Equals(path, RouteAuthorizationGuard.ChallengePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The app's own sign-in page, or else the one the host's authentication names, as an open page goes to them
        // (SignInAgainAsync). With neither there is nowhere to send them: the page is served, as to any anonymous visitor.
        var pageUrl = path + ctx.Request.QueryString.Value;
        string location;
        if (HasSignInPage(selector))
        {
            location = pathBase + RouteAuthorizationGuard.ChallengePath + "?returnUrl=" + Uri.EscapeDataString(pageUrl);
        }
        else if (await HostSignInPage.FindAsync(ctx.RequestServices).ConfigureAwait(false) is { } host)
        {
            location = host.For(pathBase + pageUrl);
        }
        else
        {
            return false;
        }

        ctx.Response.StatusCode = StatusCodes.Status302Found;
        ctx.Response.Headers.Location = location;
        ctx.Response.Headers.CacheControl = "no-store";
        return true;
    }

    private static bool HasSignInPage(RaskRootSelector selector) =>
        HasSignInPage(selector.RoutesFor(RouteAuthorizationGuard.ChallengePath));

    /// <summary>
    ///     Whether <paramref name="routes" /> has a page at the one address a reader with no sign-in is sent to, by
    ///     the route guard and here alike. A catch-all that answers "nothing here" is not one.
    /// </summary>
    internal static bool HasSignInPage(IReadOnlyList<Route> routes) =>
        RouteResolver.TryResolve(routes, RouteAuthorizationGuard.ChallengePath, out _, out var notFound) && !notFound;

    // The tab wrote it from script; the answer that removes it has no reason to stay readable by one.
    private static void ForgetEnded(HttpContext ctx, string name)
    {
#pragma warning disable S2092 // a removal, of a cookie that carries nothing: marked Secure it would not be taken over http, where the tab set it
        ctx.Response.Cookies.Delete(name, new CookieOptions { Path = "/", HttpOnly = true, Secure = ctx.Request.IsHttps });
#pragma warning restore S2092
    }
}
