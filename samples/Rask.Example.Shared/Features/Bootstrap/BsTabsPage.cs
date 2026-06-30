using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsTabsDemo" /> (BsTabs + BsAccordion, zero-JS).</summary>
[Route("bootstrap/tabs")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsTabsPage : Component
{
    protected override RenderResult Head => Title()["Tabs & accordion — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Tabs & accordion",
            "Controlled tabs and accordion — the active key / open flags live in your component, "
            + "toggled with zero JavaScript."),
        CodeSample(["BsTabsDemo.cs"], Result: BsTabsDemo())
    ];
}
