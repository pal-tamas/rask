using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/grouping")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsGroupingPage : Component
{
    protected override RenderResult Head => Title()["Grouping & list elements — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Grouping & list elements",
            "Flow grouping and lists: p, hr, pre, blockquote, div, ol/ul/li (with start/reversed/value), "
            + "dl/dt/dd, figure/figcaption."),
        H2(Class: "h4 mt-4 mb-3")["Live"],
        CodeSample(["ElementsGroupingDemo.cs"], Result: ElementsGroupingDemo())
    ];
}
