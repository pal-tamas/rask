using Rask.Data;
using Rask.Hosting.Shared;

namespace Rask.Auth;

/// <summary>Who may choose a tenant to act in, read from a Rask.Auth user. The rules are <see cref="TenantChoosing" />'s.</summary>
internal static class TenantChoice
{
    /// <summary>
    /// Whether <paramref name="user" /> may act in a tenant they chose: they belong to none of their own, and hold
    /// <see cref="AuthOptions.ActInTenantRole" />.
    /// </summary>
    internal static bool MayChoose(Authenticatable user, AuthOptions options) =>
        TenantChoosing.MayChoose(user.TenantId is not null, options.ActInTenantRole, user.IsInRole);
}
