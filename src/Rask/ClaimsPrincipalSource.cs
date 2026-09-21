using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Rask.Core.Authentication;
using Rask.Data;

namespace Rask;

/// <summary>
///     The signed-in principal of the session or request in flight, which is where <c>Current.UserId</c> and
///     the tenant a read filters by come from.
/// </summary>
/// <remarks>
///     <para>
///         The tenant is an outcome of authentication, not an input to routing: sign-in finds the user, and
///         the user says which tenant they belong to. So both travel on the <c>ClaimsPrincipal</c>, and
///         Rask.Data reads them back from here — which is what lets <c>Invoice.Read.Where(…)</c> filter
///         correctly and <c>Product.Create(…)</c> read <c>Current.UserId</c> with nothing passed to either.
///     </para>
///     <para>
///         <b>Two scopes, two sources.</b> A live session's scope holds its own <c>IUserProvider</c>, which
///         changes as the user signs in and out over one socket. A plain HTTP request's scope holds an empty
///         one, because nothing ever signs a request's provider in — there the answer is
///         <c>HttpContext.User</c>, which the request middleware hands over through <see cref="Request" />.
///     </para>
///     <para>
///         An administrator belongs to no tenant and so carries no tenant claim, and a tenant-scoped read
///         throws until they choose one. That is intended: an admin says which tenant they are acting in.
///     </para>
///     <para>
///         Lives in the meta package because it is the only assembly that can see both sides:
///         <c>IUserProvider</c> is Rask.Core's and <c>IPrincipalSource</c> is Rask.Data's, and neither of those
///         references the other.
///     </para>
/// </remarks>
internal sealed class ClaimsPrincipalSource(IUserProvider users) : IPrincipalSource
{
    /// <summary>The HTTP request this scope belongs to, or null in a live session's scope.</summary>
    /// <remarks>Held rather than its <c>User</c> copied, so a principal replaced later in the pipeline is the one read.</remarks>
    internal HttpContext? Request { get; set; }

    /// <inheritdoc />
    public ClaimsPrincipal? Current => Request is { } request ? request.User : users.Current;
}
