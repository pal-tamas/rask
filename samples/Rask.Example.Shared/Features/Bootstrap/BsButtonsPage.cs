using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsButtonsDemo" /> (BsButton/BsButtonGroup/BsBadge).</summary>
[Route("bootstrap/buttons")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsButtonsPage : Component
{
    protected override RenderResult Head => Title()["Buttons & badges — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Buttons & badges",
            "Typed Bootstrap buttons, button groups and badges — Color, Size and Outline are enums, "
            + "not class strings."),
        CodeSample(["BsButtonsDemo.cs"], Result: BsButtonsDemo())
    ];
}
