using Microsoft.Extensions.Primitives;
using Rask.Core.Routing;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class UserDetailPageTests
{
    [Theory]
    [InlineData("/users/1", "1")]
    [InlineData("/users/42", "42")]
    [InlineData("/users/ada", "ada")]
    public void RouteParam_Id_BindsFromUrl(string path, string expectedId)
    {
        var routeState = new RouteState { Path = path };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains($"User #{expectedId}", html);
        // Every component's body element gets a data-r-XXXXXXXX scope attribute,
        // so we match on the bracketed value rather than the bare <strong> tag.
        Assert.Matches($"<strong[^>]*>{expectedId}</strong>", html);
    }

    [Fact]
    public void QueryParam_Tab_BindsFromQuery()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["tab"] = "profile"
        });
        var routeState = new RouteState { Path = "/users/42", Query = query };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        // PageBinder runs through the Router and walks RouteState.Query, mapping
        // tab="profile" onto the page's [QueryParam("tab")] property. The page
        // renders the bound value inside a <strong> next to the QueryParam badge,
        // alongside the unrelated "(none)" placeholder for missing values.
        Assert.Contains("profile", html);
        Assert.DoesNotContain(">(none)<", html);
    }

    [Fact]
    public void NoQueryParam_RendersNonePlaceholder()
    {
        var routeState = new RouteState { Path = "/users/42" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));
        Assert.Contains("(none)", html);
    }
}
