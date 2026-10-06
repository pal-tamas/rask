using System.ComponentModel;
using System.Security.Claims;

namespace Rask.Cqrs;

/// <summary>
///     Decides a named authorization policy. Registered by the server host over ASP.NET's
///     <c>IAuthorizationService</c>, which Rask.Cqrs does not reference.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IPolicyEvaluator
{
    /// <summary>Whether <paramref name="user" /> meets <paramref name="policy" />.</summary>
    /// <param name="user">Who is asking.</param>
    /// <param name="policy">The policy's name.</param>
    Task<bool> Permits(ClaimsPrincipal user, string policy);
}
