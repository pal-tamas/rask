using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Rask.Data;

namespace Rask.Testing;

/// <summary>
///     <c>using var ann = Test.SignIn("ann@x.io", roles: ["Admin"], tenant: acme);</c> — the test's code runs as a signed-in
///     user until the scope is disposed.
/// </summary>
public static class SignIns
{
    extension(Test)
    {
        /// <summary>
        ///     Signs <paramref name="name" /> in for the rest of the test's flow, as Rask's sign-in would: <c>Current.UserId</c>,
        ///     <c>Current.IsInRole</c>, <c>Current.Claim</c>, the tenant filter and the audit stamps all see them, until the
        ///     returned scope is disposed.
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     await using var database = await TestDatabase.Start&lt;AppDbContext&gt;();
        ///     using var ann = Test.SignIn("ann@x.io", roles: ["Admin"], tenant: acme.Id);
        ///
        ///     var order = await Order.Place(Test.Make&lt;OrderReference&gt;());
        ///
        ///     Assert.Equal(ann.Id, (await Order.Find(order))!.CreatedBy);
        ///     </code>
        ///     <para>
        ///         The principal carries the claims a Rask sign-in issues: the user's id as <see cref="ClaimTypes.NameIdentifier" />,
        ///         <see cref="ClaimTypes.Name" />, <see cref="ClaimTypes.Email" /> when the name is an address, one
        ///         <see cref="ClaimTypes.Role" /> per role, the tenant as <see cref="Tenant.ClaimType" />, then
        ///         <paramref name="claims" />. With no <paramref name="id" />, the id is made from the name, so the same
        ///         name is the same user every time; pass a user row's id when the test reads that row as theirs.
        ///     </para>
        ///     <para>
        ///         Scoped to the test's own flow, so tests running in parallel never see each other's user. A page visited with
        ///         <c>Page.Visit(url)</c> signs in through its own app: <c>Page.Visit(url).As(ann.Principal)</c>.
        ///     </para>
        /// </remarks>
        /// <param name="name">The user's name, as their sign-in names them — usually their email address.</param>
        /// <param name="roles">Their roles; none by default.</param>
        /// <param name="tenant">Their tenant; none by default, as an administrator has none.</param>
        /// <param name="claims">Any claims of the app's own, after Rask's.</param>
        /// <param name="id">Their user id; one made from <paramref name="name" /> by default.</param>
        /// <returns>The signed-in user; dispose it to sign them out again.</returns>
        public static SignedIn SignIn(
            string name,
            IEnumerable<string>? roles = null,
            Guid? tenant = null,
            IEnumerable<Claim>? claims = null,
            Guid? id = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var userId = id ?? IdOf(name);
            return new SignedIn(userId, Principal(userId, name, roles ?? [], tenant, claims ?? []));
        }
    }

    // The same name is the same user, run after run: a version-8 GUID over the name's SHA-256.
    private static Guid IdOf(string name)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(name), hash);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }

    // As Rask.Auth's AuthPrincipal builds a signed-in user's, so the app's code reads it the same way.
    private static ClaimsPrincipal Principal(Guid userId, string name, IEnumerable<string> roles, Guid? tenant, IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity("Rask.Auth", ClaimTypes.Name, ClaimTypes.Role);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, name));
        if (name.Contains('@', StringComparison.Ordinal))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, name));
        }

        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        if (tenant is { } owner)
        {
            identity.AddClaim(new Claim(Tenant.ClaimType, owner.ToString()));
        }

        identity.AddClaims(claims);
        return new ClaimsPrincipal(identity);
    }
}
