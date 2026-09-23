using System.Security.Claims;

namespace Rask.Data;

/// <summary>
///     Who and what the work in flight is for: the signed-in user, their principal, and the tenant — read
///     from anywhere, with nothing injected.
/// </summary>
/// <remarks>
///     <para>
///         A Rask read or write is a static call that opens its own context — <c>Product.Read.Where(…)</c>,
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
///         The user is an id, not the app's <c>User</c> row: loading the row is a query, and a property that
///         silently queried the database on every read would be the wrong thing to hide. Load it when you
///         need it — <c>await User.Read.FirstOrDefaultAsync(u => u.Id == Current.UserId)</c>.
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
        ///     The id of the user the work in flight is for, or <see langword="null" /> when it is for nobody.
        /// </summary>
        /// <remarks>
        ///     An explicit <c>Current.UseUser</c> first — a background job runs for the user its row recorded,
        ///     not for whoever happens to be ambient — then the <see cref="ClaimTypes.NameIdentifier" /> claim of
        ///     <c>Current.Principal</c>.
        /// </remarks>
        public static Guid? UserId => AmbientUser.Value is { } scope ? scope.UserId : ClaimedGuid(ClaimTypes.NameIdentifier);

        /// <summary>The id of the user the work in flight is for, or a thrown exception when there is none.</summary>
        /// <exception cref="InvalidOperationException">No user is signed in and no <c>Current.UseUser</c> scope is open.</exception>
        public static Guid RequiredUserId =>
            Current.UserId ?? throw new InvalidOperationException(
                "No user is signed in, so there is no current user. Current.UserId is set for a signed-in live " +
                "session, an HTTP request and a background job enqueued by a signed-in user; anywhere else, open " +
                "Current.UseUser(id) around the work — or use Current.UserId and handle null when anonymous is fine.");

        /// <summary>
        ///     The tenant the work in flight belongs to, or <see langword="null" /> when it belongs to none.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         An explicit <see cref="Rask.Data.Tenant.Use" /> first — a job runs for the tenant its row
        ///         recorded, an admin in the tenant they chose — then the <see cref="Rask.Data.Tenant.ClaimType" />
        ///         claim of <c>Current.Principal</c>.
        ///     </para>
        ///     <para>
        ///         Null inside <see cref="Rask.Data.Tenant.Across" />: work that deliberately spans tenants is not
        ///         done on behalf of any one of them, so a row it records should not claim otherwise.
        ///     </para>
        /// </remarks>
        public static Guid? Tenant =>
            Data.Tenant.IsAcrossTenants ? null : Data.Tenant.Explicit ?? ClaimedGuid(Data.Tenant.ClaimType);

        /// <summary>The tenant the work in flight belongs to, or a thrown exception when there is none.</summary>
        /// <exception cref="InvalidOperationException">
        ///     No tenant is set, the principal carries none, or <see cref="Rask.Data.Tenant.Across" /> is open.
        /// </exception>
        public static Guid RequiredTenant =>
            Current.Tenant ?? throw new InvalidOperationException(
                "No tenant is set, so a tenant-scoped table cannot be read or written. Open one with " +
                "Tenant.Use(id) — a background job uses the tenant recorded on its own row — or say " +
                "Tenant.Across() when the work deliberately spans tenants.");

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

    private static Guid? ClaimedGuid(string claimType) =>
        Guid.TryParse(Current.Principal?.FindFirst(claimType)?.Value, out var id) ? id : null;

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
