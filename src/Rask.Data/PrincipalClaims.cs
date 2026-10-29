using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace Rask.Data;

/// <summary>
///     The user and the tenant a signed-in principal's claims name, read once per principal.
/// </summary>
/// <remarks>
///     <para>
///         For reading ahead only (<see cref="CurrentRows.Prime(IServiceProvider, CancellationToken)" />), which runs
///         before every live event: walking a principal's claims allocates an enumerator per identity, and that would
///         be paid by every event of every signed-in page.
///     </para>
///     <para>
///         A principal whose claims are changed after this read them gets its rows read ahead for the old ones — which
///         costs a query later, never a wrong answer: <c>Current.Tenant</c> and <c>Current.User</c> read the claims
///         afresh each time, and a held row is looked up by the key they name.
///     </para>
/// </remarks>
internal sealed class PrincipalClaims
{
    private static readonly ConditionalWeakTable<ClaimsPrincipal, PrincipalClaims> Read = new();

    private PrincipalClaims(ClaimsPrincipal principal)
    {
        UserId = Parsed(principal, ClaimTypes.NameIdentifier);
        Tenant = Parsed(principal, Data.Tenant.ClaimType);
    }

    /// <summary>The user the principal names.</summary>
    internal Guid? UserId { get; }

    /// <summary>The tenant the principal's claim names.</summary>
    internal Guid? Tenant { get; }

    /// <summary>The claims of the principal <paramref name="scope" /> holds, or null when it holds none.</summary>
    internal static PrincipalClaims? In(IServiceProvider? scope) =>
        Db.PrincipalIn(scope) is { } principal ? Read.GetValue(principal, static p => new PrincipalClaims(p)) : null;

    private static Guid? Parsed(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirst(claimType)?.Value, out var id) ? id : null;
}
