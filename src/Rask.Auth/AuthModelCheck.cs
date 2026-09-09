using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Auth;

/// <summary>The account tables' claim on the application's model, checked once at boot. See #1015.</summary>
/// <remarks>
/// Probes <typeparamref name="TUser" /> rather than <see cref="RaskUser" />, because the model builder
/// has the same two shapes this registration does: an app on the default user maps
/// <c>modelBuilder.AddRaskAuth()</c>, and an app with its own maps
/// <c>modelBuilder.AddRaskAuth&lt;MyUser&gt;()</c>. Probing the base type would pass on a model that
/// mapped only the derived one, which is exactly the arrangement this is meant to catch, and
/// <see cref="MapCall" /> names back whichever of the two the app should have written.
/// </remarks>
internal sealed class AuthModelCheck<TContext, TUser>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
    where TUser : RaskUser, new()
{
    protected override string Battery => "Auth";

    protected override Type Entity => typeof(TUser);

    protected override string MapCall =>
        typeof(TUser) == typeof(RaskUser) ? "AddRaskAuth" : $"AddRaskAuth<{typeof(TUser).Name}>";
}
