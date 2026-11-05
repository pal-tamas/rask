using Microsoft.AspNetCore.Http;

namespace Rask.Server.Authentication;

/// <summary>
/// The tenant choice <c>MapRaskTenantChoice</c> mapped, reachable from the sign-in relay: a live handler's
/// <c>Auth.ActInTenant</c> is redeemed on an ordinary request, where the app writes the choice into its own sign-in.
/// </summary>
internal sealed class TenantChoices
{
    private Func<HttpContext, string?, Task<int>>? _choose;

    /// <summary>Whether an app mapped a tenant choice.</summary>
    internal bool IsMapped => Volatile.Read(ref _choose) is not null;

    /// <summary>Takes the choice <c>MapRaskTenantChoice</c> mapped; an app maps one.</summary>
    internal void Map(Func<HttpContext, string?, Task<int>> choose)
    {
        if (Interlocked.CompareExchange(ref _choose, choose, null) is not null)
        {
            throw new InvalidOperationException(
                "MapRaskTenantChoice is mapped once: one place says how a chosen tenant is written into the sign-in.");
        }
    }

    /// <summary>Chooses <paramref name="tenant" /> for the request's user; the status that answers it.</summary>
    internal Task<int> Choose(HttpContext context, string? tenant) =>
        Volatile.Read(ref _choose) is { } choose ? choose(context, tenant) : Task.FromResult(StatusCodes.Status410Gone);
}
