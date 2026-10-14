using Rask.Core.Routing;

namespace Rask.Server.Tests.Authentication;

/// <summary>
///     Where a tab whose user is gone is sent is decided by the route table alone: an app with no page at the
///     sign-in address has nowhere to send them, and serves the page that was asked for.
/// </summary>
public sealed class SignInPageTests
{
    [Fact]
    public void A_table_with_a_page_at_the_sign_in_address_has_somewhere_to_send_a_reader()
    {
        IReadOnlyList<Route> routes = [new Route(typeof(Orders), "/orders"), new Route(typeof(Login), "/login")];

        var has = RaskEndpointExtensions.HasSignInPage(routes);

        Assert.True(has);
    }

    [Fact]
    public void A_table_with_no_page_at_the_sign_in_address_has_nowhere_to_send_a_reader()
    {
        IReadOnlyList<Route> routes = [new Route(typeof(Orders), "/orders")];

        var has = RaskEndpointExtensions.HasSignInPage(routes);

        Assert.False(has);
    }

    [Fact]
    public void A_sign_in_page_nested_under_a_layout_counts()
    {
        IReadOnlyList<Route> routes = [new Route(typeof(Shell), "/", [new Route(typeof(Login), "login")])];

        var has = RaskEndpointExtensions.HasSignInPage(routes);

        Assert.True(has);
    }

    // The catch-all matches every address, the sign-in one among them, and is still not a page to sign in on.
    [Fact]
    public void A_not_found_page_that_answers_the_sign_in_address_is_not_a_sign_in_page()
    {
        IReadOnlyList<Route> routes = [new Route(typeof(Orders), "/orders"), new Route(typeof(Missing), "{**__rask_notfound}")];

        var has = RaskEndpointExtensions.HasSignInPage(routes);

        Assert.False(has);
    }

    [Fact]
    public void An_empty_table_has_no_sign_in_page()
    {
        IReadOnlyList<Route> routes = [];

        var has = RaskEndpointExtensions.HasSignInPage(routes);

        Assert.False(has);
    }

    private sealed class Shell;

    private sealed class Orders;

    private sealed class Login;

    private sealed class Missing;
}
