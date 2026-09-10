using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Rask.Batteries;

namespace Rask.Auth;

/// <summary>The account tables' claim on the application's model, checked once at boot. See #1015.</summary>
/// <remarks>
/// Probes <typeparamref name="TUser" />, the app's own account type — Rask has none of its own to
/// fall back to. <c>modelBuilder.AddRaskAuth()</c> maps whichever type the generator found, so that is
/// the call this names back.
/// </remarks>
internal sealed class AuthModelCheck<TContext, TUser>(IDbContextFactory<TContext> contextFactory)
    : BatteryModelCheck<TContext>(contextFactory)
    where TContext : DbContext
    where TUser : IdentityUser, new()
{
    protected override string Battery => "Auth";

    protected override Type Entity => typeof(TUser);

    protected override string MapCall => "AddRaskAuth";
}
