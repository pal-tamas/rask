using System.Reflection;
using Rask.Core.Routing;
using Rask.Site;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

public sealed class NotFoundPageTests
{
    [Fact]
    public void Render_ShowsRouteInBody()
    {
        var routeState = new RouteState { Path = "/__unknown" };
        var html = RaskTest.Render(new global::Rask.Site.App(), TestServices.Default(routeState: routeState)).Html;

        Assert.Contains("Page not found", html);
        Assert.Contains("/__unknown", html);
        Assert.Contains(">Back to the guides<", html);
    }

    [Fact]
    public void NotFoundAttribute_AppliedToType() =>
        Assert.NotNull(typeof(NotFoundPage).GetCustomAttribute<NotFoundAttribute>());

    [Fact]
    public void HasNoParentRoute_SoItAnswersTheWholeSite()
    {
        // It used to nest under ShowcaseLayout, which covered everything while that layout was rooted at
        // "/". The layout sits at /docs now, and a nested catch-all only answers inside its parent — so
        // nesting would leave every unknown URL at the site root matching nothing and rendering an empty
        // document. Empty is worse than a 404: nothing tells the visitor, or a crawler, what happened.
        Assert.Null(typeof(NotFoundPage).GetCustomAttribute<ParentRouteAttribute>());
    }
}
