using Rask.Core.Routing;

namespace Rask.Bootstrap;

// The .navbar-brand inside a BsNavbar — the app name / logo. With Href it renders an SPA NavLink (brand
// links don't carry active styling); without, a plain span. Children are the brand content (text, a
// BsIcon, a logo image).

/// <summary>
///     The product name or logo in a navbar, normally linking home. If it is an image, it still needs alt
///     text.
/// </summary>
public sealed partial class BsNavbarBrand : BsBlock
{
    /// <summary>Where the brand links to. Conventionally the home page.</summary>
    public RouteUrl? Href { get; set; }

    protected override Component? Render() =>
        Href is { } href
            ? NavLink.Href(href).ActiveClass("").Class(BsClass.Join("navbar-brand", Class))[Items]
            : Span.Id(Id).Class(BsClass.Join("navbar-brand", Class))[Items];
}
