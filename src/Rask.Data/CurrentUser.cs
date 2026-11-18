using System.Globalization;
using System.Security.Claims;

namespace Rask.Data;

/// <summary>
///     Who the work in flight is for: the signed-in user and their principal — read from anywhere, with nothing
///     injected.
/// </summary>
/// <remarks>
///     <para>
///         A Rask read or write is a static call that opens its own context — <c>Product.Where(…)</c>,
///         a <c>Product.Create(…)</c> factory — so there is no constructor to inject a user into. These values
///         are ambient instead, and flow on <see cref="AsyncLocal{T}" /> so they follow an <c>await</c>:
///         <code>
/// public static Product Create(string name) => new()
/// {
///     Name = name,
///     OwnerId = Current.RequiredUserId,
/// };
///         </code>
///     </para>
///     <para>
///         <b>Where they are set.</b> A Rask app sets them for a live session's work, for every HTTP request
///         (an API endpoint, a CQRS endpoint) and for a background job, which runs as the user and tenant its
///         row recorded when it was enqueued. Outside all of those — a hosted service's own loop, startup — the
///         answers are <see langword="null" />, and the <c>Required…</c> forms throw rather than let an
///         anonymous write through looking like a signed-in one.
///     </para>
///     <para>
///         The user's and the tenant's own rows are <c>Current.User</c> and <c>Current.Tenant</c>, once the app's
///         aggregates say <see cref="IUser" /> and <see cref="ITenant" />: the build generates them, typed as the
///         aggregate's read face, and they come from a cache the framework fills as the work starts.
///     </para>
/// </remarks>
// Rask.Wire declares Current — every app has it, and it carries Current.Cancellation. The data layer adds who the
// work is for, as static extensions, so the call site stays Current.UserId wherever Rask.Data is referenced.
public static class CurrentUser
{
    private static readonly AsyncLocal<UserScope?> AmbientUser = new();

    extension(Current)
    {
        /// <summary>
        ///     The signed-in principal of the session or request in flight, or <see langword="null" /> when the
        ///     work is not running for one.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Anonymous work in a session or request still has a principal — an unauthenticated one — so
        ///         <c>Current.Principal?.Identity?.IsAuthenticated</c> is the sign-in check, and
        ///         <c>Current.UserId</c> is null for it.
        ///     </para>
        ///     <para>
        ///         <c>Current.UseUser</c> does not change this. A job run for a user has that user's id and no
        ///         principal: nobody signed in to run it, and a principal rebuilt from the id would claim roles
        ///         the job never checked.
        ///     </para>
        /// </remarks>
        public static ClaimsPrincipal? Principal => Db.PrincipalFromScope();

        /// <summary>
        ///     The value of the first <paramref name="type" /> claim <c>Current.Principal</c> carries, or
        ///     <see langword="null" /> when it carries none or nobody is signed in.
        /// </summary>
        /// <param name="type">The claim type, such as <c>"department"</c> or <see cref="ClaimTypes.Email" />.</param>
        /// <returns>The claim's value, or <see langword="null" />.</returns>
        /// <remarks>
        ///     Read from the principal of the work in flight, so it answers the same on the first GET, inside a live
        ///     event — after the session revalidated the user — and in an HTTP request:
        ///     <code>
        ///     var department = Current.Claim("department");
        ///     </code>
        ///     A background job has no principal (see <c>Current.Principal</c>), so it answers
        ///     <see langword="null" /> there.
        /// </remarks>
        public static string? Claim(string type)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(type);
            return Current.Principal?.FindFirst(type)?.Value;
        }

        /// <summary>
        ///     Whether the signed-in user holds <paramref name="role" />; <see langword="false" /> when nobody is
        ///     signed in.
        /// </summary>
        /// <param name="role">The role, as the principal's role claims name it.</param>
        /// <returns><see langword="true" /> when the signed-in principal is in the role.</returns>
        /// <remarks>
        ///     <c>ClaimsPrincipal.IsInRole</c> on <c>Current.Principal</c>, answered only for an authenticated one:
        ///     an anonymous principal is never in a role, whatever claims it carries. Like <c>Current.Claim</c>, it
        ///     answers the same on the first GET and inside a live event, and <see langword="false" /> in a
        ///     background job, which has no principal.
        ///     <code>
        ///     if (Current.IsInRole("Admin")) { … }
        ///     </code>
        /// </remarks>
        public static bool IsInRole(string role)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(role);
            return Current.Principal is { Identity.IsAuthenticated: true } principal && principal.IsInRole(role);
        }

        /// <summary>
        ///     The id of the user the work in flight is for, or <see langword="null" /> when it is for nobody.
        /// </summary>
        /// <remarks>
        ///     An explicit <c>Current.UseUser</c> first — a background job runs for the user its row recorded,
        ///     not for whoever happens to be ambient — then the <see cref="ClaimTypes.NameIdentifier" /> claim of
        ///     <c>Current.Principal</c>.
        ///     <para>
        ///         An app whose user aggregate is keyed by an <see cref="int" /> or a <see cref="long" /> — say
        ///         <c>User : Aggregate&lt;int&gt;, IUser</c> — has the build generate <c>Current.UserId</c> as that
        ///         number instead, which hides this one. Its <see cref="ClaimTypes.NameIdentifier" /> is
        ///         the number written out: <c>"12"</c>.
        ///     </para>
        /// </remarks>
        public static Guid? UserId => Id;

        /// <summary>The id of the user the work in flight is for, or a thrown exception when there is none.</summary>
        /// <exception cref="InvalidOperationException">No user is signed in and no <c>Current.UseUser</c> scope is open.</exception>
        public static Guid RequiredUserId => RequiredId;

        /// <summary>
        ///     Makes <paramref name="userId" /> the current user until the returned scope is disposed.
        /// </summary>
        /// <param name="userId">The user to work for, or <see langword="null" /> to work for nobody.</param>
        /// <returns>A scope that restores the previous user.</returns>
        /// <remarks>
        ///     What a background job does before running its handler, and what a test does instead of signing
        ///     somebody in. It wins over the signed-in principal, and it changes <c>Current.UserId</c> only —
        ///     never <c>Current.Principal</c>.
        /// </remarks>
        public static IDisposable UseUser(Guid? userId) => new Scope(new UserScope(userId));
    }

    /// <summary>
    ///     The user the work in flight is for, as the <see cref="Guid" /> the framework carries it in — the one the
    ///     framework's own rows record, whatever type the app's user is keyed by.
    /// </summary>
    internal static Guid? Id => UserIdIn(Db.ScopeServices);

    /// <summary><see cref="Id" />, or a thrown exception when the work is for nobody.</summary>
    /// <exception cref="InvalidOperationException">No user is signed in and no <c>Current.UseUser</c> scope is open.</exception>
    internal static Guid RequiredId =>
        Id ?? throw new InvalidOperationException(
            "No user is signed in, so there is no current user. Current.UserId is set for a signed-in live " +
            "session, an HTTP request and a background job enqueued by a signed-in user; anywhere else, open " +
            "Current.UseUser(id) around the work — or use Current.UserId and handle null when anonymous is fine.");

    /// <summary><c>Current.UserId</c>, with <paramref name="scope" /> standing for the scope <see cref="Db.UseScope" /> opened.</summary>
    /// <param name="scope">The scope whose principal answers.</param>
    /// <param name="claims">The principal's claims as read ahead, or null to read them now.</param>
    internal static Guid? UserIdIn(IServiceProvider? scope, PrincipalClaims? claims = null)
    {
        if (AmbientUser.Value is { } explicitUser)
        {
            return explicitUser.UserId;
        }

        return claims is not null ? claims.UserId : ClaimedGuid(scope, ClaimTypes.NameIdentifier);
    }

    internal static Guid? ClaimedGuid(IServiceProvider? scope, string claimType) =>
        FromClaim(Db.PrincipalIn(scope)?.FindFirst(claimType)?.Value, NumberedFor(claimType));

    /// <summary>The user a <see cref="ClaimTypes.NameIdentifier" /> claim's value names, or null when it names none.</summary>
    /// <param name="value">The claim's value.</param>
    internal static Guid? FromClaim(string? value) => FromClaim(value, CurrentRows.NumberedUser);

    /// <summary>
    ///     The <see cref="Guid" /> a claim's value names: a <see cref="Guid" /> written out, or — only when
    ///     <paramref name="numbered" /> — a number written out, carried as <see cref="TenantNumber" /> carries one.
    /// </summary>
    /// <param name="value">The claim's value.</param>
    /// <param name="numbered">Whether the aggregate the claim names is keyed by an int or a long.</param>
    /// <remarks>
    ///     Gated on the key type, so an app whose users are Guids keeps reading a numeric claim as nobody rather
    ///     than as a Guid no row of its holds.
    /// </remarks>
    internal static Guid? FromClaim(string? value, bool numbered)
    {
        if (Guid.TryParse(value, out var id))
        {
            return id;
        }

        return numbered && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? TenantNumber.ToGuid(number)
            : null;
    }

    /// <summary>
    ///     <paramref name="userId" /> as a <see cref="ClaimTypes.NameIdentifier" /> claim writes it: the number,
    ///     for an app whose users are numbered, and the <see cref="Guid" /> otherwise.
    /// </summary>
    internal static string ToClaim(Guid userId) =>
        CurrentRows.NumberedUser && TenantNumber.TryRead(userId, out var number)
            ? number.ToString(CultureInfo.InvariantCulture)
            : userId.ToString();

    private static bool NumberedFor(string claimType) =>
        string.Equals(claimType, Tenant.ClaimType, StringComparison.Ordinal)
            ? CurrentRows.NumberedTenant
            : string.Equals(claimType, ClaimTypes.NameIdentifier, StringComparison.Ordinal) && CurrentRows.NumberedUser;

    private sealed record UserScope(Guid? UserId);

    private sealed class Scope : IDisposable
    {
        private readonly UserScope? _previous;
        private bool _disposed;

        internal Scope(UserScope state)
        {
            _previous = AmbientUser.Value;
            AmbientUser.Value = state;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                AmbientUser.Value = _previous;
            }
        }
    }
}
