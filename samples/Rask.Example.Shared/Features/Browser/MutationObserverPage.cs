using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="MutationObserverDemo" /> (<c>IMutationObserver</c>).</summary>
[Route("browser/mutation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class MutationObserverPage : Component
{
    protected override RenderResult Head => Title()["Mutation observer — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Mutation observer",
            "Be notified when an element's children, attributes, or text content change via "
            + "IMutationObserver — for reacting to DOM written by a third-party script or a portal you "
            + "don't own. The browser pushes each change to C#, which re-renders through the framework. "
            + "Works on both transports."),
        CodeSample(
            ["MutationObserverDemo.cs"],
            Notes: "ObserveAsync(ElementRef, handler, options?) returns a disposable observation; the handler "
                + "is pushed from JS via a static [JSInvokable], so one wiring serves both Server and WASM.",
            Result: MutationObserverDemo())
    ];
}
