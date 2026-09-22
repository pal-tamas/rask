using System.Security.Claims;
using Rask.Core.Authentication;

namespace Rask.Core.Tests.Authentication;

public class AuthSignInTests
{
    [Fact]
    public async Task Signing_in_outside_a_handler_throws()
    {
        var auth = new AuthSignIn();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task Signing_out_outside_a_handler_throws()
    {
        var auth = new AuthSignIn();

        await Assert.ThrowsAsync<InvalidOperationException>(() => auth.SignOutAsync());
    }

    [Fact]
    public async Task Signing_in_inside_a_handler_stores_a_pending_sign_in()
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
    public async Task Signing_out_inside_a_handler_stores_a_pending_sign_out_with_no_principal()
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
    public async Task Consuming_twice_returns_false_the_second_time()
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
    public async Task Signing_in_a_null_principal_throws()
    {
        var auth = new AuthSignIn();

        using (auth.EnterHandler())
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => auth.SignInAsync(null!));
        }
    }

    [Fact]
    public async Task Signing_in_after_the_handler_scope_is_disposed_throws()
    {
        var auth = new AuthSignIn();

        using (auth.EnterHandler())
        {
            await auth.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.SignInAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
