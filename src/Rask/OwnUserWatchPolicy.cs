using System.Security.Claims;
using Rask.Cqrs;
using Rask.Data;

namespace Rask;

/// <summary>
///     The watch policy every scope has until the app writes its own: a signed-in user may watch notifications keyed by
///     their own id, and nothing else.
/// </summary>
/// <remarks>
///     <para>
///         It is what lets the most common subscription need no policy at all — a job reporting progress to the user who
///         started it, a notification bell:
///     </para>
///     <code>
///     public sealed record ExportProgress([For&lt;User&gt;] Guid UserId, int Percent) : INotification;
///
///     var progress = QueryClient.Subscribe&lt;ExportProgress&gt;(Current.RequiredUserId);
///     </code>
///     <para>
///         Registered as an open generic, so a policy the app writes for a scope — <c>IWatchPolicy&lt;Order&gt;</c> —
///         replaces it for that scope alone. For any other scope it still refuses every key that is not the subscriber's
///         own id, so a forgotten policy fails closed. User ids are <see cref="Guid" />s, so a scope keyed by something
///         else is never matched by accident.
///     </para>
/// </remarks>
/// <typeparam name="TScope">What the watched key identifies.</typeparam>
internal sealed class OwnUserWatchPolicy<TScope>(IPrincipalSource principals) : IWatchPolicy<TScope>
{
    public Task<bool> CanWatchAsync(object key, CancellationToken cancellationToken) =>
        Task.FromResult(
            key is Guid id
            && principals.Current?.FindFirst(ClaimTypes.NameIdentifier)?.Value is { } claim
            && Guid.TryParse(claim, out var user)
            && user == id);
}
