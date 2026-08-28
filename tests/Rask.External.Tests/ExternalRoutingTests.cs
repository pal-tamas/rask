using System.Text;
using System.Text.Json;
using Rask.Core.Routing;

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
