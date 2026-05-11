using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Core.Tests.Authentication;

public class AuthSignInTests
{
    [Fact]
    public async Task SignInAsync_OutsideHandler_Throws()
    {
        var auth = new AuthSignIn();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task SignOutAsync_OutsideHandler_Throws()
    {
        var auth = new AuthSignIn();
        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.SignOutAsync());
    }

    [Fact]
    public async Task SignInAsync_InsideHandler_StoresPending()
    {
        var auth = new AuthSignIn();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "Test"));

        using (auth.EnterHandler())
        {
            await auth.SignInAsync(principal, "/dashboard", "Test");
        }

        Assert.True(auth.TryConsume(out var pending));
        Assert.Equal(AuthAction.SignIn, pending.Action);
        Assert.Same(principal, pending.Principal);
        Assert.Equal("/dashboard", pending.ReturnUrl);
        Assert.Equal("Test", pending.Scheme);
    }

    [Fact]
    public async Task SignOutAsync_InsideHandler_StoresPendingWithNullPrincipal()
    {
        var auth = new AuthSignIn();
        using (auth.EnterHandler())
        {
            await auth.SignOutAsync("/bye");
        }

        Assert.True(auth.TryConsume(out var pending));
        Assert.Equal(AuthAction.SignOut, pending.Action);
        Assert.Null(pending.Principal);
        Assert.Equal("/bye", pending.ReturnUrl);
    }

    [Fact]
    public async Task TryConsume_Twice_SecondReturnsFalse()
    {
        var auth = new AuthSignIn();
        using (auth.EnterHandler())
        {
            await auth.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        Assert.True(auth.TryConsume(out _));
        Assert.False(auth.TryConsume(out _));
    }

    [Fact]
    public async Task SignInAsync_NullPrincipal_Throws()
    {
        var auth = new AuthSignIn();
        using (auth.EnterHandler())
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => auth.SignInAsync(null!));
        }
    }

    [Fact]
    public async Task SignInAsync_AfterHandlerScopeDisposed_Throws()
    {
        var auth = new AuthSignIn();
        using (auth.EnterHandler())
        {
            await auth.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
