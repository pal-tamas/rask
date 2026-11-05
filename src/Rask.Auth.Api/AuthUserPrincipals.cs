using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>The principal a user of this app signs in as — the one <see cref="AuthPrincipal" /> builds at sign-in.</summary>
/// <remarks>
/// An account that signs in by name reads that name through its column's mapping, so the model is asked for it; one that
/// signs in with its address needs no context at all.
/// </remarks>
internal sealed class AuthUserPrincipals<TContext, TUser, TKey>(IDbContextFactory<TContext> contexts) : IPrincipalFor
    where TContext : DbContext
    where TUser : Authenticatable<TKey>
    where TKey : notnull
{
    public ClaimsPrincipal? For(object user)
    {
        if (user is not TUser account)
        {
            return null;
        }

        if (AccountName<TUser, TKey>.IsEmail)
        {
            return AuthPrincipal.For(account, account.Email);
        }

        using var db = contexts.CreateDbContext();
        return AuthPrincipal.For(account, AccountName<TUser, TKey>.Of(db.Model, account));
    }
}
