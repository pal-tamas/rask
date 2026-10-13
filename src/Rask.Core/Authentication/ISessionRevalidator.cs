using System.Security.Claims;

namespace Rask.Core.Authentication;

/// <summary>
/// Asks whether a live session's principal is still signed in, and with which claims.
/// </summary>
/// <remarks>
/// <para>
/// A live page holds the principal its connection opened with for as long as that connection stays open, which can be
/// hours: nothing is read from the browser again. So the host asks this instead, where a request would have gone through
/// the app's authentication: as a connection attaches, before every handler and every navigation, and before a render
/// the server starts itself when nothing has asked within the last second. A session ended elsewhere (signed out on
/// another device, a password reset), an expired token or a removed role then stops a page that is still open at its
/// next event.
/// </para>
/// <para>
/// It is called on the event's own path, so it is as cheap as the app makes it. Handing back the instance it was given
/// costs the host nothing; a principal with the same claims costs a comparison; one with other claims replaces the
/// session's and renders the page again. An implementation that throws refuses the event.
/// </para>
/// <para>
/// Rask.Auth provides one. A host with its own sign-in registers its own; without one nothing is asked.
/// </para>
/// </remarks>
public interface ISessionRevalidator
{
    /// <summary>The principal as it stands now, or <see langword="null" /> when its session has ended.</summary>
    /// <param name="principal">
    /// The principal the session holds: the one its connection carried, or the one this last returned in its place.
    /// </param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>
    /// <paramref name="principal" /> itself when nothing changed; another principal for the same user when their claims
    /// did, which the session then acts under; <see langword="null" /> when they are no longer signed in, which sends
    /// the page to the sign-in page.
    /// </returns>
    ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
