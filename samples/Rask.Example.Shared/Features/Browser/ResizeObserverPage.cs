using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="ResizeObserverDemo" /> (<c>IResizeObserver</c>).</summary>
[Route("browser/resize")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ResizeObserverPage : Component
{
    protected override RenderResult Head => Title()["Resize observer — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Resize observer",
            "Be notified when an element's size changes via IResizeObserver — for container-responsive "
            + "layouts or re-laying-out a canvas/chart. The browser pushes each size to C#, which re-renders "
            + "through the framework. The sibling of the intersection observer. Works on both transports."),
        CodeSample(
            ["ResizeObserverDemo.cs"],
            Notes: "ObserveAsync(ElementRef, handler) returns a disposable observation and fires once "
                + "initially with the current size; the handler is pushed from JS via a static [JSInvokable].",
            Result: ResizeObserverDemo())
    ];
}
