using System.Text;
using System.Text.Json;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.TestSupport;

namespace Rask.External.Tests;

/// <summary>A React-rendered page that owns a route outright.</summary>
[Route("/reports/{id:int}")]
public sealed partial class Report : ReactComponent
{
    /// <summary>Bound from the route's own path segment.</summary>
    [RouteParam] public int Id { get; set; }
}

/// <summary>An ordinary Rask layout with an outlet.</summary>
public sealed partial class ReportLayout : Component
{
    protected override Component? Render() => Div.Class("shell")[Outlet];
}

/// <summary>A React-rendered page nested inside that layout.</summary>
[Route("/reports/{id:int}/detail")]
[ParentRoute(typeof(ReportLayout))]
public sealed partial class ReportDetail : ReactComponent
{
    /// <summary>Bound from the route's own path segment.</summary>
    [RouteParam] public int Id { get; set; }
}

/// <summary>A React-rendered page that names itself in the document's head.</summary>
[Route("/reports/{id:int}/titled")]
public sealed partial class TitledReport : ReactComponent
{
    /// <summary>Bound from the route's own path segment.</summary>
    [RouteParam] public int Id { get; set; }

    protected override Component? HeadAssets => Title[$"Report {Id}"];
}

/// <summary>A Rask page holding one island twice and another beside them.</summary>
public sealed partial class ThreeReports : Component
{
    protected override Component? Render() => Div[Report.Id(1), Report.Id(2), TitledReport.Id(3)];
}

// Whether an external component is routable is not a design intention, it is a fact about whether
// three generators agree: RoutesGenerator has to treat it as a page, the external generator has to
// complete it, and the chain has to reach it. Each runs independently, so this is asserted rather
// than assumed — "React owns this route" is the headline case for the whole feature, and it would be
// a poor thing to discover was only true of a hand-written wrapper page.
public partial class ExternalRoutingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_route_on_an_external_component_generates_its_url()
    {
        // The generated static extension, exactly as for any other page.
        Assert.Equal("/reports/41", global::Rask.External.Tests.Report.Url(41));
    }

    [Fact]
    public void A_routed_external_component_still_renders_its_host_element()
    {
        var html = Render(Report.Id(41));

        Assert.StartsWith("<rask-external ", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-opaque", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Report\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_route_parameter_arrives_as_a_prop()
    {
        // The part worth having: a route segment is an ordinary C# property, so it is also an ordinary
        // prop. The front end receives /reports/41 as `id: 41` with no plumbing in between.
        var html = Render(Report.Id(41));

        using var props = JsonDocument.Parse(ReadProps(html));
        Assert.Equal(41, props.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void A_parent_route_generates_a_url_under_its_layout()
    {
        Assert.Equal("/reports/41/detail", global::Rask.External.Tests.ReportDetail.Url(41));
    }

    [Fact]
    public void A_routed_island_that_sets_a_title_still_loads_the_runtime()
    {
        // The runtime script used to BE the island's HeadAssets, so overriding it for a title replaced the
        // script: the page rendered its host element and nothing ever mounted into it.
        var page = TitledReport.Id(41);

        var html = RenderPage(page);

        Assert.Contains($"src=\"{ExternalDefaults.RuntimeScriptUrl}\"", Head(html), StringComparison.Ordinal);
    }

    [Fact]
    public void A_routed_islands_title_reaches_the_page_head()
    {
        // An island takes the serializer's element branch, which never reads HeadAssets, so the island hands
        // its own over.
        var page = TitledReport.Id(41);

        var html = RenderPage(page);

        Assert.Contains(">Report 41</title>", Head(html), StringComparison.Ordinal);
    }

    [Fact]
    public void Several_islands_on_a_page_emit_the_runtime_script_once()
    {
#pragma warning disable RASK014 // the test hands the very instance it renders to the renderer
        var page = new ThreeReports();
#pragma warning restore RASK014

        var html = RenderPage(page);

        Assert.Equal(3, Count(html, "<rask-external "));
        Assert.Equal(1, Count(html, ExternalDefaults.RuntimeScriptUrl));
    }

#pragma warning disable RASK014 // the document shell every host installs, built by hand around the page under test
    private static string RenderPage(Component page) =>
        new RootErrorBoundary(page).RenderAsLiveRoot(RenderHarness.EmptyServices());
#pragma warning restore RASK014

    private static string Head(string html) =>
        html[html.IndexOf("<head>", StringComparison.Ordinal)..html.IndexOf("</head>", StringComparison.Ordinal)];

    private static int Count(string html, string value) => html.Split(value).Length - 1;

    private static string Render(Component component)
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(component, sb);
        return sb.ToString();
    }

    private static string ReadProps(string html)
    {
        const string marker = "props=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf('"', start);
        return System.Net.WebUtility.HtmlDecode(html[start..end]);
    }
}
