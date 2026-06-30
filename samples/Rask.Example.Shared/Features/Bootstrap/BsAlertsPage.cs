using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsAlertsDemo" /> (BsAlert, dismissible).</summary>
[Route("bootstrap/alerts")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsAlertsPage : Component
{
    protected override RenderResult Head => Title()["Alerts — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Alerts",
            "Contextual feedback messages. The dismissible alert closes with zero JavaScript — "
            + "OnClose flips your state and the runtime re-renders."),
        CodeSample(["BsAlertsDemo.cs"], Result: BsAlertsDemo())
    ];
}
