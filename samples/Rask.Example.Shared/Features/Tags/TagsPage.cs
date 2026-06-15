using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("tags")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class TagsPage : Component
{
    protected override RenderResult Head => Title()["Tag factories — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Tag factories",
            "Every standard HTML element has a generator-emitted factory in Rask.Core.Components.Generated. " +
            "Tag-specific attributes come first; the universal Id/Class/Style/Data trail at the end."),
        H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-fonts text-accent me-2"), "Text & semantic"],
        CodeSample(
            ["TagsTextDemo.cs"],
            Result: TagsTextDemo()),
        H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-input-cursor-text text-accent me-2"), "Forms"],
        CodeSample(
            ["TagsFormDemo.cs"],
            Result: TagsFormDemo()),
        H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-table text-accent me-2"), "Tables"],
        CodeSample(
            ["TagsTableDemo.cs"],
            Result: TagsTableDemo()),
        H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-image text-accent me-2"), "Media"],
        CodeSample(
            ["TagsMediaDemo.cs"],
            Result: TagsMediaDemo()),
        H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-dash-circle text-accent me-2"), "Void elements"],
        CodeSample(
            ["TagsVoidDemo.cs"],
            Notes:
            "Void elements (Br, Hr, Img, Meta, Link, Input, …) have SelfClosing => true and never accept children.",
            Result: TagsVoidDemo())
    ];
}
