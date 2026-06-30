using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsUtilitiesDemo" /> (typed utility classes + Bs.Join).</summary>
[Route("bootstrap/utilities")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsUtilitiesPage : Component
{
    protected override RenderResult Head => Title()["Utility classes — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Utility classes",
            "Typed Bootstrap utilities — Shadow, Border, Margin/Padding, Display, Flex, Rounded, Txt, "
            + "Sizing, Position, Bg — composed into a Class with Bs.Join. Responsive variants take a Bp "
            + "breakpoint (Bp.Md → the -md- infix). No stringly-typed class names."),
        CodeSample(["BsUtilitiesDemo.cs"], Result: BsUtilitiesDemo())
    ];
}
