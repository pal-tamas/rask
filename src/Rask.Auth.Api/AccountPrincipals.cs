using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Auth;

/// <summary>A user's principal as their account issues it now: roles, tenant and own claims from the row.</summary>
internal sealed class AccountPrincipals<TUser>(IAuthContexts contexts) : IAccountPrincipals
    where TUser : Authenticatable
{
    public async Task<ClaimsPrincipal?> Of(Guid userId, CancellationToken cancellationToken)
    {
        var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var user = await db.Set<TUser>().AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                .ConfigureAwait(false);
            return user is null ? null : AuthPrincipal.For(user);
        }
    }
}
