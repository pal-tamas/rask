using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="NavigatorInfoDemo" /> (<c>INavigatorInfo</c>).</summary>
[Route("browser/navigator-info")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class NavigatorInfoPage : Component
{
    protected override RenderResult Head => Title()["Browser info — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Browser info",
            "Read-only navigator facts via INavigatorInfo — onLine, language, userAgent."),
        CodeSample(
            ["NavigatorInfoDemo.cs"],
            Notes: "These are property reads the invoke dispatcher returns directly — no JS helper needed.",
            Result: NavigatorInfoDemo())
    ];
}
