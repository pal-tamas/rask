using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsModalDemo" /> (BsModal, zero-JS).</summary>
[Route("bootstrap/modal")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsModalPage : Component
{
    protected override RenderResult Head => Title()["Modal — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Modal",
            "A controlled modal with backdrop and click-outside-to-close — driven by Rask's live "
            + "runtime, with no bootstrap.js loaded."),
        CodeSample(["BsModalDemo.cs"], Result: BsModalDemo())
    ];
}
