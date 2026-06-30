using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Bootstrap section — <see cref="BsNavDemo" /> (BsNavbar + BsNav/BsNavItem, SPA-routed).</summary>
[Route("bootstrap/nav")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class BsNavPage : Component
{
    protected override RenderResult Head => Title()["Navbar & nav — Bootstrap — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Navbar & nav",
            "Typed navigation: BsNavbar is the top-bar shell; BsNav holds BsNavItems, each of which "
            + "renders a SPA-routed NavLink that highlights itself by matching the current route. "
            + "Pair BsOffcanvas(Responsive: Bp.Md) with them for a sidebar that collapses to a drawer "
            + "on mobile — exactly how this showcase's own chrome is built."),
        CodeSample(["BsNavDemo.cs"], Result: BsNavDemo())
    ];
}
