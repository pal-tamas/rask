using System.Globalization;
using Rask.Data;

namespace Rask.Auth;

/// <summary>Who may choose a tenant to act in, and what a chosen tenant may be.</summary>
internal static class TenantChoice
{
    /// <summary>
    /// Whether <paramref name="user" /> may act in a tenant they chose: they belong to none of their own, and hold
    /// <see cref="AuthOptions.ActInTenantRole" />.
    /// </summary>
    internal static bool MayChoose(Authenticatable user, AuthOptions options) =>
        user.TenantId is null && options.ActInTenantRole is { } role && user.IsInRole(role);

    /// <summary>The tenant numbered <paramref name="number" />, carried the way <c>Tenant.Use(long)</c> carries it.</summary>
    internal static Guid Of(long number) => TenantNumber.ToGuid(number);

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
