using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Auth;

/// <summary>The principal a user of this app signs in as — the one <see cref="AuthPrincipal" /> builds at sign-in.</summary>
internal sealed class AuthUserPrincipals : IPrincipalFor
{
    public ClaimsPrincipal? For(object user) => user is Authenticatable account ? AuthPrincipal.For(account) : null;
}
