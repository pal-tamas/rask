using Rask.Cqrs;
using Rask.Data;

namespace Rask.Background;

/// <summary>
///     Which work a reader of the bell may see (#1368), decided in their own scope: their own, in their tenant, and their
///     tenant's shared work (<c>.ByTenant()</c>) of every job kind whose handler's <c>[Authorize]</c> admits them — the same
///     check, through the same <see cref="RequestAccess" />, that a dispatch of that job makes.
/// </summary>
/// <remarks>
///     Fails closed: a kind whose policy cannot be decided here — nothing registered to decide it — is refused, and a reader
///     with no principal (an id alone, as <c>Current.UseUser</c> gives) passes no <c>[Authorize]</c> at all.
/// </remarks>
internal static class WorkAccess
{
    /// <summary>Who reads in <paramref name="scope" />, or null for nobody signed in.</summary>
    public static async Task<WorkViewer?> Viewer(IServiceProvider scope, CancellationToken cancellationToken)
    {
        if (CurrentUser.UserIdIn(scope) is not { } user)
        {
            return null;
        }

        return new WorkViewer(user, Tenant.KeyIn(scope), await Refused(scope, user, cancellationToken).ConfigureAwait(false));
    }

    // Every registered job kind that declares [Authorize] and does not admit the reader. Their rights are their principal's —
    // only when it IS them: under Current.UseUser(other) the signed-in principal's rights are not that other user's.
    private static async Task<HashSet<string>> Refused(IServiceProvider scope, Guid user, CancellationToken cancellationToken)
    {
        var refused = new HashSet<string>(StringComparer.Ordinal);
        var principal = Db.PrincipalIn(scope) is { } signedIn && PrincipalClaims.In(scope)?.UserId == user ? signedIn : null;
        foreach (var (kind, type) in JobSerializerRegistry.Registered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CqrsRegistry.FindAuthorization(type) is not { } declared)
            {
                continue;
            }

            if (principal is null || !await Admits(principal, type.Name, declared, scope).ConfigureAwait(false))
            {
                refused.Add(kind);
            }
        }

        return refused;
    }

    private static async Task<bool> Admits(
        System.Security.Claims.ClaimsPrincipal principal, string name, RequestAuthorization declared, IServiceProvider scope)
    {
        try
        {
            return await RequestAccess
                .Refusal(principal, name, requiresAuthentication: true, declared.RoleSets, declared.Policies, scope)
                .ConfigureAwait(false) is null;
        }
        catch (InvalidOperationException)
        {
            return false; // a policy nothing here can decide: not shown, rather than shown unchecked
        }
    }
}
