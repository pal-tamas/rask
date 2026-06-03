using System.Security.Claims;

namespace Rask.Core.Authentication;

public sealed class AuthSignIn : IAuthSignIn
{
    private bool _inHandler;
    private PendingAuth? _pending;

    public Task SignInAsync(ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null)
    {
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(principal);
        _pending = PendingAuth.SignIn(principal, returnUrl, scheme);
        return Task.CompletedTask;
    }

    public Task SignOutAsync(string? returnUrl = null, string? scheme = null)
    {
        EnsureInHandler();
        _pending = PendingAuth.SignOut(returnUrl, scheme);
        return Task.CompletedTask;
    }

    internal IDisposable EnterHandler()
    {
        _inHandler = true;
        return new HandlerScope(this);
    }

    internal bool TryConsume(out PendingAuth pending)
    {
        if (_pending is null)
        {
            pending = default!;
            return false;
        }

        pending = _pending;
        _pending = null;
        return true;
    }

    private void EnsureInHandler()
    {
        if (!_inHandler)
        {
            throw new InvalidOperationException(
                "AuthSignIn can only be used from event handlers. " +
                "Calling it during component Render() or initial GET is not supported.");
        }
    }

    private sealed class HandlerScope(AuthSignIn auth) : IDisposable
    {
        public void Dispose() => auth._inHandler = false;
    }
}

public enum AuthAction
{
    SignIn,
    SignOut
}

public sealed record PendingAuth(
    AuthAction Action,
    ClaimsPrincipal? Principal,
    string? ReturnUrl,
    string? Scheme)
{
    internal static PendingAuth SignIn(ClaimsPrincipal principal, string? returnUrl, string? scheme) =>
        new(AuthAction.SignIn, principal, returnUrl, scheme);

    internal static PendingAuth SignOut(string? returnUrl, string? scheme) =>
        new(AuthAction.SignOut, null, returnUrl, scheme);
}
