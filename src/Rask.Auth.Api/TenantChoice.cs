using Rask.Data;
using Rask.Hosting.Shared;
using Rask.Wire;

namespace Rask.Auth;

/// <summary>Who may choose a tenant to act in, read from a Rask.Auth user. The rules are <see cref="TenantChoosing" />'s.</summary>
internal static class TenantChoice
{
    /// <summary>The answer to a choice of a tenant no row names: refused, as a value that names no tenant is.</summary>
    internal static AuthResult Unknown { get; } = AuthResult.Fail(AuthError.NotAllowed, "No tenant has that id.");

    /// <summary>
    /// Whether <paramref name="tenant" /> is a tenant the app has: its tenant aggregate (the one that says
    /// <see cref="ITenant" />) holds a row keyed by it. True when no aggregate says it, so there is nothing to ask.
    /// </summary>
    internal static async ValueTask<bool> ExistsAsync(Guid tenant, CancellationToken cancellationToken) =>
        await CurrentRows.TenantExists(tenant, cancellationToken).ConfigureAwait(false) ?? true;

    /// <summary>
    /// Whether <paramref name="user" /> may act in a tenant they chose: they belong to none of their own, and hold
    /// <see cref="AuthOptions.ActInTenantRole" />.
    /// </summary>
    internal static bool MayChoose<TKey>(Authenticatable<TKey> user, AuthOptions options)
        where TKey : notnull =>
        TenantChoosing.MayChoose(user.TenantId is not null, options.ActInTenantRole, user.IsInRole);
}
