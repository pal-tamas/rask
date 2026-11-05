using System.Globalization;
using Microsoft.AspNetCore.Http;
using Rask.Data;

namespace Rask.Hosting.Shared;

/// <summary>
/// The rules every tenant choice keeps, whoever signs the user in: Rask.Auth's <c>POST /api/auth/tenant</c> and the
/// endpoint <c>MapRaskTenantChoice</c> maps for an app with its own sign-in. One copy, so the two cannot drift.
/// </summary>
internal static class TenantChoosing
{
    /// <summary>
    /// Whether a user may choose a tenant to act in: they belong to none of their own, and hold
    /// <paramref name="role" />. A <see langword="null" /> role turns choosing off.
    /// </summary>
    internal static bool MayChoose(bool belongsToTenant, string? role, Func<string, bool> isInRole) =>
        !belongsToTenant && role is not null && isInRole(role);

    /// <summary>
    /// Whether the browser says the request came from this app's own page. A caller that sends no
    /// <c>Sec-Fetch-Site</c> is not a browser, and carries its credential deliberately.
    /// </summary>
    /// <remarks>
    /// Switching the tenant an administrator reads and writes in is not something another site does for them, even one
    /// an app's CORS policy lets send headers.
    /// </remarks>
    internal static bool FromThisSite(HttpRequest request) =>
        request.Headers["Sec-Fetch-Site"] is not { Count: > 0 } site
        || string.Equals(site.ToString(), "same-origin", StringComparison.Ordinal);

    /// <summary>The tenant numbered <paramref name="number" />, carried the way <c>Tenant.Use(long)</c> carries it.</summary>
    internal static Guid Of(long number) => TenantNumber.ToGuid(number);

    /// <summary>The number <paramref name="tenant" /> carries, when it is a numbered tenant.</summary>
    internal static bool TryNumber(Guid tenant, out long number) => TenantNumber.TryRead(tenant, out number);

    /// <summary>
    /// Whether <paramref name="tenant" /> can name a tenant: neither <see cref="Guid.Empty" />, which the batteries read
    /// as "no tenant", nor <see cref="Guid.AllBitsSet" />, which a read compares against to match nothing.
    /// </summary>
    internal static bool IsTenant(Guid tenant) => tenant != Guid.Empty && tenant != Guid.AllBitsSet;

    /// <summary>
    /// Reads the tenant a request names — a <see cref="Guid" />, or a tenant's number — into <paramref name="tenant" />;
    /// <see langword="null" /> or empty names none. False when it is neither.
    /// </summary>
    internal static bool TryRead(string? value, out Guid? tenant)
    {
        tenant = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (Guid.TryParse(value, out var id))
        {
            tenant = id;
        }
        else if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0)
        {
            tenant = Of(number);
        }

        return tenant is { } named && IsTenant(named);
    }
}
