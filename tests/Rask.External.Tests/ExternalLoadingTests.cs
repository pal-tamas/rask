using Rask.Core.Routing;

namespace Rask.External.Tests;

/// <summary>An island that IS the route: the router builds it, so it sets its own placeholder.</summary>
[Route("/reports/{id:int}/skeleton")]
public sealed partial class SkeletonReport : ReactComponent
{
    public SkeletonReport() => Loading = Div.Class("skeleton");

    /// <summary>Bound from the route's own path segment.</summary>
    [RouteParam] public int Id { get; set; }
}

// What `Loading` puts in the first response. The client half — that the placeholder goes at the first
// mount and not before — is ExternalRuntimeTests, against the shipped runtime.
public partial class ExternalLoadingTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Loading_renders_inside_the_host_element()
    {
        var island = Report.Id(41).Loading(Div.Class("skeleton h-64 w-full"));

        var html = IslandHtml.Render(island);

        Assert.EndsWith("data-rask-opaque><div class=\"skeleton h-64 w-full\"></div></rask-external>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_never_travels_in_the_props()
    {
        var island = Report.Id(41).Loading(Div.Class("skeleton")["Report 41"]);

        var props = IslandHtml.ReadProps(IslandHtml.Render(island));

        Assert.Equal("""{"id":41}""", props);
    }

    [Fact]
    public void An_island_without_Loading_renders_an_empty_host()
    {
        var island = Report.Id(41);

        var html = IslandHtml.Render(island);

        Assert.EndsWith("data-rask-opaque></rask-external>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_inside_Loading_is_encoded()
    {
        var island = Report.Id(41).Loading(P["<b>Q3 & Q4</b>"]);

        var html = IslandHtml.Render(island);

        Assert.Contains("<p>&lt;b&gt;Q3 &amp; Q4&lt;/b&gt;</p></rask-external>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_set_in_the_constructor_survives_a_second_render()
    {
        // Constructed, not chained: the router activates a page and binds its route parameters, so nothing
        // resets what the constructor set. (A chain does — `SkeletonReport.Id(41)` renders an empty host,
        // which is why an island placed by a chain takes `.Loading(…)` as a step instead.)
#pragma warning disable RASK014 // built the way the router builds a routed page
        var page = new SkeletonReport { Id = 41 };
#pragma warning restore RASK014

        var first = IslandHtml.RenderLive(page);
        var second = IslandHtml.RenderLive(page);

        Assert.Contains("<div class=\"skeleton\"></div></rask-external>", first, StringComparison.Ordinal);
        Assert.Contains("<div class=\"skeleton\"></div></rask-external>", second, StringComparison.Ordinal);
    }

    [Fact]
    public void Loading_is_a_step_on_another_runtimes_island_too()
    {
        // Inherited from the base class across an assembly boundary, like Hydration: the chain reads it from
        // metadata, where being nullable is what makes it an optional step rather than a required one.
        var island = Tile.Caption("Revenue").Loading(Span["loading"]);

        var html = IslandHtml.Render(island);

        Assert.Contains("<span>loading</span></rask-external>", html, StringComparison.Ordinal);
    }
}
