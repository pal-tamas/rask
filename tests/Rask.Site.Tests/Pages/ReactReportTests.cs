using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Site.Features.Islands;

namespace Rask.Site.Tests.Pages;

/// <summary>
///     The whole-page island, as the first response carries it: named in the head, bootable, and not blank.
/// </summary>
/// <remarks>
///     Built with <c>new</c>, the way the router builds a routed page — a chain would reset the
///     <c>Loading</c> the island gives itself.
/// </remarks>
public sealed class ReactReportTests
{
    [Fact]
    public void The_page_island_names_the_document_and_still_loads_the_island_runtime()
    {
        var html = Html();

        var head = System.Net.WebUtility.HtmlDecode(html[..html.IndexOf("</head>", StringComparison.Ordinal)]);

        Assert.Contains(">A React island as a whole page in C# — Rask</title>", head, StringComparison.Ordinal);
        Assert.Contains("/_content/Rask.External/rask-external.js", head, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_response_holds_a_placeholder_inside_the_host_element()
    {
        var html = Html();

        var host = html[html.IndexOf("<rask-external ", StringComparison.Ordinal)..html.IndexOf("</rask-external>", StringComparison.Ordinal)];
        Assert.Contains("name=\"ReactReport\"", host, StringComparison.Ordinal);
        Assert.Contains("data-rask-opaque><", host, StringComparison.Ordinal);
    }

    [Fact]
    public void The_link_back_is_a_prop_holding_the_url_the_host_serves()
    {
        var html = Html();

        Assert.Contains("&quot;back&quot;:&quot;/docs/islands/&quot;", html, StringComparison.Ordinal);
    }

#pragma warning disable RASK014 // built the way the router builds a routed page
    private static string Html() =>
        new RootErrorBoundary(new ReactReport()).RenderAsLiveRoot(new ServiceCollection().BuildServiceProvider());
#pragma warning restore RASK014
}
