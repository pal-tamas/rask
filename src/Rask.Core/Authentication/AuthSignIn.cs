using System.Security.Claims;

namespace Rask.Core.Authentication;

public sealed class AuthSignIn : IAuthSignIn
{
    private bool _inHandler;
    private PendingAuth? _pending;

    // Whether the host keeps a session cookie (AddRaskCookieSession), which a sign-in with a token is written into.
    internal bool KeepsTokens { get; init; }

    public Task SignIn(
        ClaimsPrincipal principal, string? returnUrl = null, string? scheme = null, bool persistent = false)
    {
        EnsureInHandler();
        ArgumentNullException.ThrowIfNull(principal);
        _pending = PendingAuth.SignIn(principal, returnUrl, scheme, persistent);
        return Task.CompletedTask;
    }

    public Task SignInWithToken(string token, string? returnUrl = null)
    {
        EnsureInHandler();
        ArgumentException.ThrowIfNullOrEmpty(token);
        if (!KeepsTokens)
        {
            throw new InvalidOperationException(
                "SignInWithToken writes the token into the session cookie, and this host keeps none. Add one with " +
                "builder.Services.AddRaskCookieSession(o => o.Name = \"…\"), naming the cookie your authentication scheme reads.");
        }

        _pending = PendingAuth.SignInWithToken(token, returnUrl);
        return Task.CompletedTask;
    }

    public Task SignOut(string? returnUrl = null, string? scheme = null)
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
                "AuthSignIn works only inside an event handler (a click, a form submit). " +
                "Move the call out of Render() and the first-load hooks into the handler that signs the user in.");
        }
    }

    private sealed class HandlerScope(AuthSignIn auth) : IDisposable
    {
        public void Dispose() => auth._inHandler = false;
    }
}
