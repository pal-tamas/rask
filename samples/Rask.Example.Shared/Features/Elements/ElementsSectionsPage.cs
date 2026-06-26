using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/sections")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsSectionsPage : Component
{
    protected override RenderResult Head => Title()["Sections & heading elements — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Sections & heading elements",
            "Document structure: h1–h6, hgroup, and the semantic landmarks article, section, nav, aside, "
            + "header, footer, main, address, search."),
        H2(Class: "h4 mt-4 mb-3")["Live"],
        CodeSample(["ElementsSectionsDemo.cs"], Result: ElementsSectionsDemo())
    ];
}
