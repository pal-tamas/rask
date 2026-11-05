using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Server.Authentication;

namespace Rask.Server;

// The sign-in relay's HTTP half: a live handler cannot write a Set-Cookie, so the ticket it was handed is redeemed here,
// on an ordinary request from the page, where the cookie is written or cleared.
public static partial class RaskEndpointExtensions
{
    private static async Task RedeemAuthTicketAsync(HttpContext ctx, IAuthTicketStore tickets)
    {
        // The answer sets or clears a credential: no cache, shared or the browser's, keeps it.
        ctx.Response.Headers.CacheControl = "no-store";

        // Defense-in-depth CSRF: the redeem ticket is a single-use, session-bound, 128-bit secret
        // delivered only over the authenticated same-origin WS frame, so classic cookie-CSRF can't
        // forge it. As belt-and-braces we still reject a cross-origin Origin/Referer, and a browser that says the
        // request came from anywhere but this origin — bounced before the (otherwise antiforgery-exempt) ticket is
        // consulted, so a forged attempt does not burn it either.
        if (!IsSameOrigin(ctx.Request) || !FetchedBySameOrigin(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var (ticketId, sessionId) = await ReadRedeemRequestAsync(ctx).ConfigureAwait(false);
        if (string.IsNullOrEmpty(ticketId) || string.IsNullOrEmpty(sessionId))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ctx.Response.StatusCode = tickets.TryRedeem(ticketId, sessionId, out var ticket)
            ? await ApplyTicketAsync(ctx, ticket).ConfigureAwait(false)
            : StatusCodes.Status410Gone;
    }

    // Signs the ticket's sign-in or sign-out into this response; the status the redeem answers.
    private static async Task<int> ApplyTicketAsync(HttpContext ctx, AuthTicket ticket)
    {
        var cookieSession = ctx.RequestServices.GetService<CookieSession>();
        if (ticket.Token is { } token)
        {
            // The app's own token goes into the session cookie, and the page loads again under it: no reconnect is
            // expected, so no attach window opens for a principal Rask cannot know.
            if (cookieSession is null)
            {
                return StatusCodes.Status410Gone;
            }

            cookieSession.Write(ctx.Response, token);
            return StatusCodes.Status200OK;
        }

        var scheme = await ResolveAuthSchemeAsync(ctx.RequestServices, ticket.Scheme).ConfigureAwait(false);
        if (ticket.Action == AuthAction.SignIn)
        {
            // Persistence travels with the ticket: a "remember me" sign-in from a component gets the same
            // browser-outliving cookie the /api/auth login endpoint writes.
            await ctx.SignInAsync(
                    scheme,
                    ticket.Principal!,
                    new AuthenticationProperties { IsPersistent = ticket.Persistent })
                .ConfigureAwait(false);
        }
        else
        {
            await SignOutAsync(ctx, scheme, cookieSession).ConfigureAwait(false);
        }

        // The reconnect that follows carries this principal, not the session's owner, so let the hello
        // admission check expect it (MayAttach). Opened here rather than when the handler issued the
        // ticket: the client reconnects only once this response arrives, and tying the window to the
        // redeem means only whoever holds the ticket can open it. A sign-out opened at issue time let
        // any anonymous holder of the session id attach before the client had even redeemed (#1075).
        if (ctx.RequestServices.GetService<LiveSessionStore>()?.Peek(ticket.SessionId) is { } handoffSession)
        {
            handoffSession.PendingAuthHandoff = ticket.Principal ?? new ClaimsPrincipal(new ClaimsIdentity());
        }

        return StatusCodes.Status200OK;
    }

    // A host with a session cookie clears it, and signs its scheme out only when that scheme can: a JWT bearer scheme,
    // or a handler that reads the cookie itself, has nothing of its own to clear and would throw.
    private static async Task SignOutAsync(HttpContext ctx, string scheme, CookieSession? cookieSession)
    {
        if (cookieSession is not null)
        {
            cookieSession.Clear(ctx.Response);
            if (ctx.RequestServices.GetService<IAuthenticationHandlerProvider>() is not { } handlers
                || await handlers.GetHandlerAsync(ctx, scheme).ConfigureAwait(false) is not IAuthenticationSignOutHandler)
            {
                return;
            }
        }

        await ctx.SignOutAsync(scheme).ConfigureAwait(false);
    }

    // A browser says where a request came from in Sec-Fetch-Site, and another site cannot make it say otherwise; the
    // page's own fetch says "same-origin". Absent — a browser that predates the header, or no browser at all — the
    // Origin/Referer check and the ticket's secrecy stand alone, as they did before.
    private static bool FetchedBySameOrigin(HttpRequest request) =>
        request.Headers["Sec-Fetch-Site"].ToString() is var site
        && (site.Length == 0 || string.Equals(site, "same-origin", StringComparison.OrdinalIgnoreCase));

    // The ticket and the session it was issued for; both null when the body is not JSON.
    private static async Task<(string? TicketId, string? SessionId)> ReadRedeemRequestAsync(HttpContext ctx)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted).ConfigureAwait(false);
            return (StringProperty(doc.RootElement, "ticket"), StringProperty(doc.RootElement, "session"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
