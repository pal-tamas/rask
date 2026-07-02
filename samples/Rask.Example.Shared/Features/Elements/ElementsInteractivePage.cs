using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("elements/interactive")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ElementsInteractivePage : Component
{
    protected override Component? Head => Title()["Interactive elements — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "Interactive elements",
            "Native interactive elements: details/summary (a no-JS disclosure), dialog, and menu."),
        H2(Class: "h4 mt-4 mb-3")["Live"],
        CodeSample(["ElementsInteractiveDemo.cs"], Result: ElementsInteractiveDemo())
    ];
}
