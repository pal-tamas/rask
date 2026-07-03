using Rask.Core.Routing;

namespace Rask.Bootstrap.Tests;

// Static-render assertions for BsNavbarBrand: an SPA NavLink when Href is set, a plain span otherwise.
public class BsNavbarBrandTests
{
    [Fact]
    public void WithHref_RendersNavLinkWithBrandClass()
    {
        var html = BsNavbarBrand(Href: new RouteUrl("/"))["Acme"].ToHtml();

        Assert.Contains("navbar-brand", html);
        Assert.Contains("href=\"/\"", html);
        Assert.Contains("data-rask-nav", html);
        Assert.Contains("Acme", html);
    }

    [Fact]
    public void WithoutHref_RendersPlainSpan()
    {
        var html = BsNavbarBrand()["Acme"].ToHtml();

        Assert.Contains("<span", html);
        Assert.Contains("navbar-brand", html);
        Assert.Contains("Acme", html);
        Assert.DoesNotContain("<a ", html);
    }

    [Fact]
    public void ExtraClass_IsMergedWithBrandClass()
    {
        var html = BsNavbarBrand(Class: "fw-bold")["Acme"].ToHtml();

        Assert.Contains("navbar-brand", html);
        Assert.Contains("fw-bold", html);
    }
}
