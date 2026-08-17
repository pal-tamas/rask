using Rask.Core.Routing;
using Rask.Html.Components;

namespace Rask.Bootstrap;

// A Bootstrap nav list: <ul class="nav">. Holds BsNavItem children. Set Vertical for a stacked sidebar
// nav (.flex-column), or Pills/Tabs/Underline for the matching visual style; Fill/Justified spread the
// items across the available width.

/// <summary>
///     A navigation list, rendered as tabs, pills, or plain links.
/// </summary>
public sealed partial class BsNav : BsBlock
{
    /// <summary>Stacks the entries vertically.</summary>
    public bool? Vertical { get; set; }

    /// <summary>Renders the entries as pills.</summary>
    public bool? Pills { get; set; }

    /// <summary>Renders the entries as tabs.</summary>
    public bool? Tabs { get; set; }

    /// <summary>Renders the entries with an underline indicator.</summary>
    public bool? Underline { get; set; }

    /// <summary>Spreads the entries to fill the available width, proportionally to their text.</summary>
    public bool? Fill { get; set; }

    /// <summary>Spreads the entries to fill the width, each the same size.</summary>
    public bool? Justified { get; set; }

    protected override Component? Render() => Ul
        .Id(Id)
        .Class(BsClass.Join(
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

/// <summary>
///     One entry in a <c>BsNav</c>, active automatically when its route matches the current page.
/// </summary>
public sealed partial class BsNavItem : BsBlock
{
    /// <summary>Where the entry goes, as a generated route URL.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>
    ///     The URL to compare against the current location, when it differs from <c>Href</c>.
    /// </summary>
    public RouteUrl? Match { get; set; }

    /// <summary>Makes the entry non-interactive.</summary>
    public bool? Disabled { get; set; }

    /// <summary>
    ///     Whether the whole path must match, or only a prefix — a prefix keeps a section's entry active on
    ///     its child pages.
    /// </summary>
    public NavLinkMatch? ActiveMatch { get; set; }

    protected override Component? Render()
    {
        var linkCls = BsClass.Join("nav-link", Disabled is true ? "disabled" : null, Class);
        Component link = Href is { } href
            ? NavLink.Href(href).Match(Match).ActiveClass("active").ActiveMatch(ActiveMatch).Class(linkCls)[Items]
            : Span.Class(linkCls)[Items];
        return Li.Class("nav-item")[link];
    }
}
