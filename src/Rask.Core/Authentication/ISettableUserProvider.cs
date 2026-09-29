using System.Security.Claims;

namespace Rask.Core.Authentication;

/// <summary>A host's user provider, whose principal a test signs in by setting — as a real sign-in hands it over.</summary>
internal interface ISettableUserProvider
{
    void Set(ClaimsPrincipal user);
}
