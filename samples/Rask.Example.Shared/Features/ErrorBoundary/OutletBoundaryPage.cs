using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("outlet-boundary")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class OutletBoundaryPage : Component
{
    // Flipped by the button; the next render throws so we can show that the failure is
    // contained to the outlet region rather than taking down the whole shell.
    private bool _crashed;

    protected override RenderResult Head => Title()["Outlet boundary — Rask"];

    protected override RenderResult Render()
    {
        if (_crashed)
        {
            throw new InvalidOperationException(
                "This page threw during render — the outlet's default error boundary caught it.");
        }

        return
        [
            PageHeader.Render(
                "Default outlet boundary",
                "Every page rendered into an Outlet() is wrapped in an error boundary by default. When a page crashes, the failure is contained to the content region — the top nav and the sidebar stay live — instead of replacing the whole page shell. The boundary clears automatically when you navigate away, so a crash never sticks over the next page. Opt out per outlet with Outlet(DisableErrorBoundary: true)."),
            CodeSample(
                ["OutletBoundaryPage.cs"],
                Notes:
                "Click to make this page throw on its next render. Notice the sidebar and top nav remain usable — pick any other section to recover (navigating clears the boundary).",
                Result: Button(
                    Class: "btn btn-danger",
                    Id: "outlet-crash-trigger",
                    OnClick: () => _crashed = true)[I(Class: "bi bi-bug me-2"), "Crash this page"])
        ];
    }
}
