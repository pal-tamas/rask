using System.Security.Claims;

namespace Rask.Data;

/// <summary>
/// Signs a principal into one service scope that belongs to no request or session — a job's — so everything in
/// it (<c>Current.Principal</c>, an <c>[Authorize]</c> check, a page rendered to print) sees that user. The
/// server host supplies it; a host without one leaves background work anonymous.
/// </summary>
internal interface IScopeUser
{
    /// <summary>Makes <paramref name="user"/> the signed-in user of this scope.</summary>
    void SignIn(ClaimsPrincipal user);
}
