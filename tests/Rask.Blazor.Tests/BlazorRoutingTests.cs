using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>A Blazor-rendered page that owns a route outright and names itself in the head.</summary>
[Route("/greetings/{count:int}")]
public sealed partial class GreetingPage : BlazorComponent<Greeting>
{
    /// <summary>Bound from the route's own path segment, and handed on as the hosted parameter.</summary>
    [RouteParam] public int? Count { get; set; }

    protected override Component? HeadAssets => [base.HeadAssets, Title[$"Greeting {Count}"]];
}

// The Blazor mirror of ExternalRoutingTests: a hosted component as the whole page, rather than as a part
// of one. Unlike a JavaScript island it is rendered on the server, so the page is in the first response.
public partial class BlazorRoutingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_route_on_a_blazor_island_generates_its_url()
    {
        Assert.Equal("/greetings/41", global::Rask.Blazor.Tests.GreetingPage.Url(41));
    }

    [Fact]
    public void A_route_parameter_arrives_as_a_blazor_parameter()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var html = Page.Render(GreetingPage.Count(41), services).Html;

        Assert.Contains("<p class=\"greeting\">(none)/41</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_routed_blazor_islands_title_reaches_the_page_head()
    {
        var services = new ServiceCollection().BuildServiceProvider();

#pragma warning disable RASK014 // the document shell every host installs, built by hand around the page under test
        var html = new RootErrorBoundary(GreetingPage.Count(41)).RenderAsLiveRoot(services);
#pragma warning restore RASK014

        Assert.Contains(">Greeting 41</title>", html[..html.IndexOf("</head>", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void A_titled_blazor_island_keeps_the_librarys_stylesheet_by_including_the_base_head()
    {
        // A Blazor island's HeadAssets IS where RaskBlazorOptions.HeadAssets reaches the page, so an
        // override that leaves the base out drops the hosted library's stylesheet.
        var services = new ServiceCollection()
            .AddRaskBlazor(o => o.HeadAssets.Add(Link.Rel("stylesheet").Href("/lib.css")))
            .BuildServiceProvider();

#pragma warning disable RASK014 // the document shell every host installs, built by hand around the page under test
        var html = new RootErrorBoundary(GreetingPage.Count(41)).RenderAsLiveRoot(services);
#pragma warning restore RASK014

        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        Assert.Contains("href=\"/lib.css\"", head, StringComparison.Ordinal);
        Assert.Contains(">Greeting 41</title>", head, StringComparison.Ordinal);
    }
}
