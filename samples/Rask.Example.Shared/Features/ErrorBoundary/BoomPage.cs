using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("boom")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BoomPage : Component
{
    protected override RenderResult Head => Title()["Error boundary — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Error boundary",
            "ErrorBoundary catches exceptions thrown by descendants — render-time, sync lifecycle, async lifecycle, and event handlers — and renders a fallback in their place. The fallback receives a Recover() callback so the boundary can be reset from a button click."),
        H2(Class: "h4 mt-4 mb-3")["Handler throw — boundary catches and renders fallback"],
        CodeSample(
            EmbeddedSource.Read("BoomHandlerDemo.cs"),
            Result: BoomHandlerDemo()),
        H2(Class: "h4 mt-5 mb-3")["Render-time throw"],
        CodeSample(
            EmbeddedSource.Read("BoomRenderDemo.cs"),
            Notes:
            "The same boundary catches synchronous exceptions thrown inside a descendant's Render(). Click below to flip a flag; the next render of the child throws and the fallback replaces it.",
            Result: BoomRenderDemo()),
        H2(Class: "h4 mt-5 mb-3")["Nested boundaries — inner catches first"],
        CodeSample(
            EmbeddedSource.Read("BoomNestedDemo.cs"),
            Notes:
            "The inner boundary catches first — the outer healthy region (and its sibling paragraph) stays mounted. If the inner fallback itself throws, the outer boundary catches the escalation.",
            Result: BoomNestedDemo())
    ];
}
