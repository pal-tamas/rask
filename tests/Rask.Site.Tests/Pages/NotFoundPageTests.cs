using System.Reflection;
using Rask.Core.Routing;
using Rask.Site;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

public sealed class NotFoundPageTests
{
    [Fact]
    public void The_not_found_page_shows_the_route_in_its_body()
    {
        var routeState = new RouteState { Path = "/__unknown" };

        var html = Test.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.Contains("Page not found", html);
        Assert.Contains("/__unknown", html);
        Assert.Contains(">Back to guides<", html);
    }

    [Fact]
    public void The_page_type_carries_the_NotFound_attribute() =>
        Assert.NotNull(typeof(NotFoundPage).GetCustomAttribute<NotFoundAttribute>());

    [Fact]
    public void It_has_no_parent_route_so_it_answers_the_whole_site()
    {
        // It used to nest under ShowcaseLayout, which covered everything while that layout was rooted at
        // "/". The layout sits at /docs now, and a nested catch-all only answers inside its parent — so
        // nesting would leave every unknown URL at the site root matching nothing and rendering an empty
        // document. Empty is worse than a 404: nothing tells the visitor, or a crawler, what happened.
        Assert.Null(typeof(NotFoundPage).GetCustomAttribute<ParentRouteAttribute>());
    }
}
