using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="PerformanceDemo" /> (<c>IPerformance</c>).</summary>
[Route("browser/performance")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PerformancePage : Component
{
    protected override RenderResult Head => Title()["Performance — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Performance",
            "A high-resolution monotonic clock and page-load timing via IPerformance (the Performance / "
            + "Navigation Timing API) — time an operation, or report real-user metrics (TTFB, DOMContentLoaded, "
            + "load). Works on both transports."),
        CodeSample(
            ["PerformanceDemo.cs"],
            Notes: "NowAsync() returns performance.now() (monotonic, sub-ms); GetNavigationTimingAsync() "
                + "returns the navigation entry's milestones, or null when none is available yet.",
            Result: PerformanceDemo())
    ];
}
