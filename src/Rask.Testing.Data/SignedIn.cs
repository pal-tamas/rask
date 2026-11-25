using System.Security.Claims;
using Rask.Data;

namespace Rask.Testing;

/// <summary>The user <c>Test.SignIn(…)</c> signed in for the test; disposing it signs them out again.</summary>
public sealed class SignedIn : IDisposable
{
    private readonly IDisposable _scope;

    internal SignedIn(Guid id, ClaimsPrincipal principal)
    {
        Id = id;
        Principal = principal;
        _scope = Current.UsePrincipal(principal);
    }

    /// <summary>Their user id — what <c>Current.UserId</c> and the audit stamps read.</summary>
    public Guid Id { get; }

    /// <summary>Their principal, with every claim a Rask sign-in issues — for <c>Page.Visit(url).As(…)</c>.</summary>
    public ClaimsPrincipal Principal { get; }

    /// <summary>Signs them out: the flow's principal is what it was before.</summary>
    public void Dispose() => _scope.Dispose();
}
