using Rask.Core.Routing;

namespace Rask.Bootstrap;

// A Bootstrap nav list: <ul class="nav">. Holds BsNavItem children. Set Vertical for a stacked sidebar
// nav (.flex-column), or Pills/Tabs/Underline for the matching visual style; Fill/Justified spread the
// items across the available width.
public sealed class BsNav : BsBlock
{
    public bool? Vertical { get; set; }
    public bool? Pills { get; set; }
    public bool? Tabs { get; set; }
    public bool? Underline { get; set; }
    public bool? Fill { get; set; }
    public bool? Justified { get; set; }

    protected override RenderResult Render() => Ul(Id: Id, Class: BsClass.Join(
        "nav",
        Vertical is true ? "flex-column" : null,
        Pills is true ? "nav-pills" : null,
        Tabs is true ? "nav-tabs" : null,
        Underline is true ? "nav-underline" : null,
        Fill is true ? "nav-fill" : null,
        Justified is true ? "nav-justified" : null,
        Class))[Items];
}

// One nav entry: <li class="nav-item"> wrapping a .nav-link. With Href it renders a core NavLink, so the
// link is SPA-routed (data-rask-nav) and lights up the .active class itself by matching the current
// route — pass ActiveMatch: Prefix to keep a parameterised section active across its sub-routes. Without
// Href it renders a plain .nav-link span (a non-navigating label). Disabled greys it out.
public sealed class BsNavItem : BsBlock
{
    public RouteUrl? Href { get; set; }
    public RouteUrl? Match { get; set; }
    public bool? Disabled { get; set; }
    public NavLinkMatch? ActiveMatch { get; set; }

    protected override RenderResult Render()
    {
        var linkCls = BsClass.Join("nav-link", Disabled is true ? "disabled" : null, Class);
        Child link = Href is { } href
            ? NavLink(Href: href, Match: Match, ActiveClass: "active", ActiveMatch: ActiveMatch, Class: linkCls)[Items]
            : Span(Class: linkCls)[Items];
        return Li(Class: "nav-item")[link];
    }
}
