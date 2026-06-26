using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/tables")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsTablesPage : Component
{
    protected override RenderResult Head => Title()["Table elements — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Table elements",
            "The table family: table, caption, colgroup/col (span), thead/tbody/tfoot, tr, th (scope), "
            + "td (colspan/rowspan/headers)."),
        H2(Class: "h4 mt-4 mb-3")["Live"],
        CodeSample(["ElementsTablesDemo.cs"], Result: ElementsTablesDemo())
    ];
}
