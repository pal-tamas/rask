using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Server;

/// <summary>
///     The <see cref="ISessionRevalidator" /> <c>AddSessionRevalidator&lt;T&gt;()</c> registers: finds the principal's
///     user, loads them through the app's <see cref="SessionRevalidator{TUser}" /> or takes the one kept, and asks the
///     app what principal they act under.
/// </summary>
internal sealed class KeptSessionRevalidator(SessionRevalidator app, KeptSessionUsers users) : ISessionRevalidator
{
    public async ValueTask<ClaimsPrincipal?> Revalidate(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // Nobody signed in: there is no sign-in to end.
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        // Signed in as nobody in particular: no user to load, so nothing stands.
        if (RaskEndpointExtensions.UserKey(principal) is not { Length: > 0 } userId)
        {
            return null;
        }

        return await users.Get(userId, app, cancellationToken).ConfigureAwait(false) is { } user
            ? app.PrincipalOf(user, principal)
            : null;
    }
}
