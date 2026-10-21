using System.Security.Claims;

namespace Rask.Data;

/// <summary>
/// The principal a user signs in as TODAY, rebuilt from their account — what a job reads to learn which of the
/// rights its enqueuer had are still theirs. The auth battery supplies it; an app without one has none.
/// </summary>
internal interface IAccountPrincipals
{
    /// <summary>The user's principal from their account now, or null when there is no such account any more.</summary>
    Task<ClaimsPrincipal?> Of(Guid userId, CancellationToken cancellationToken);
}
