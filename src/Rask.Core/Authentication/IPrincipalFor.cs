using System.ComponentModel;
using System.Security.Claims;

namespace Rask.Core.Authentication;

/// <summary>
///     Turns one of the app's users into the principal the app issues when that user signs in — what a test's
///     <c>Page.Visit(url).As(user)</c> signs in with.
/// </summary>
/// <remarks>
///     Machinery: the auth battery owns the user row, so it supplies this; an app does not implement it.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IPrincipalFor
{
    /// <summary>The principal <paramref name="user" /> signs in as.</summary>
    /// <param name="user">A row of the app's user table.</param>
    /// <returns>The principal, or <c>null</c> when <paramref name="user" /> is not one of this app's users.</returns>
    ClaimsPrincipal? For(object user);
}
