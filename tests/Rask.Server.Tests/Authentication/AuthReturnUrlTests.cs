using Rask.Core.Authentication;
using Rask.Server.Authentication;

namespace Rask.Server.Tests.Authentication;

/// <summary>Where the browser lands once a live sign-in or sign-out is redeemed.</summary>
public class AuthReturnUrlTests
{
    [Theory]
    [InlineData("/login")]
    [InlineData("/login?returnUrl=%2Forders")]
    [InlineData("/LOGIN/")]
    [InlineData("/login#form")]
    public void A_sign_in_that_returns_to_the_sign_in_page_falls_back_to_the_root(string returnUrl)
    {
        var landing = AuthReturnUrl.For(AuthAction.SignIn, returnUrl, route: null);

        Assert.Equal("/", landing);
    }

    [Theory]
    [InlineData(AuthAction.SignIn, "/_rask/auth/redeem")]
    [InlineData(AuthAction.SignIn, "/_rask/auth/redeem?ticket=abc")]
    [InlineData(AuthAction.SignOut, "/_rask/auth/redeem")]
    [InlineData(AuthAction.SignOut, "/_rask/auth/who?x=1")]
    [InlineData(AuthAction.SignOut, "/_rask")]
    public void A_return_to_rasks_own_endpoints_falls_back_to_the_root(AuthAction action, string returnUrl)
    {
        var landing = AuthReturnUrl.For(action, returnUrl, route: null);

        Assert.Equal("/", landing);
    }

    [Theory]
    [InlineData(AuthAction.SignIn, "/orders?page=2", "/orders?page=2")]
    [InlineData(AuthAction.SignIn, "/login-help", "/login-help")]
    [InlineData(AuthAction.SignIn, "/_raskish", "/_raskish")]
    [InlineData(AuthAction.SignOut, "/login", "/login")]
    [InlineData(AuthAction.SignOut, "/login?returnUrl=%2Forders", "/login?returnUrl=%2Forders")]
    public void A_page_to_land_on_is_kept(AuthAction action, string returnUrl, string expected)
    {
        var landing = AuthReturnUrl.For(action, returnUrl, route: null);

        Assert.Equal(expected, landing);
    }
}
