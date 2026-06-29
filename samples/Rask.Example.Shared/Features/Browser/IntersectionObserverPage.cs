using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="IntersectionObserverDemo" /> (<c>IIntersectionObserver</c>).</summary>
[Route("browser/intersection")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class IntersectionObserverPage : Component
{
    protected override RenderResult Head => Title()["Intersection observer — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Intersection observer",
            "Be notified when an element enters or leaves the viewport via IIntersectionObserver — for "
            + "lazy-loading, infinite scroll, or reveal-on-scroll. The browser pushes each change to C#, "
            + "which re-renders through the framework. Works on both transports."),
        CodeSample(
            ["IntersectionObserverDemo.cs"],
            Notes: "ObserveAsync(ElementRef, handler, options?) returns a disposable observation; the handler "
                + "is pushed from JS via a static [JSInvokable], so one wiring serves both Server and WASM.",
            Result: IntersectionObserverDemo())
    ];
}
