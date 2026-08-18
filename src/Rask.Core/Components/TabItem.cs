using Rask.Core.Routing;

namespace Rask.Core.Components;

/// <summary>
///     One tab in a <see cref="TabStrip" /> — a label + icon that navigates to a route when tapped. On the web
///     hosts it renders a real link; inside a native shell it becomes a <c>UITabBarItem</c> (iOS) or a bottom
///     navigation item (Android).
/// </summary>
/// <remarks>
///     <see cref="To" /> is a <see cref="RouteUrl" />, so pass a generated <c>Features.Routes.Home()</c> rather
///     than a string — a tab is exactly the kind of long-lived reference that a renamed route should break at
///     compile time.
/// </remarks>
public sealed partial class TabItem : Component
{
    /// <summary>
    ///     The tab's label. Required — it is the tab's accessible name on every host. <c>new</c> for the
    ///     same reason as <see cref="AppBar.Title" />.
    /// </summary>
    public required new string Title { get; set; }

    /// <summary>The tab's icon. Required.</summary>
    public required BarIcon Icon { get; set; }

    /// <summary>The type-safe route this tab navigates to (e.g. <c>Features.Routes.Home()</c>). Required.</summary>
    public required RouteUrl To { get; set; }

    /// <summary>
    ///     An optional badge (an unread count like <c>"3"</c> or <c>"99+"</c>). Rendered beside the label on
    ///     the web and projected to <c>UITabBarItem.BadgeValue</c> / an icon overlay on native. <c>null</c> or
    ///     empty means no badge.
    /// </summary>
    public string? Badge { get; set; }

    // A tab never renders on its own: the web markup is emitted by the owning TabStrip, which is the only place
    // that knows which tab is selected (and so which link carries aria-current). Rendering here would mean
    // either duplicating that knowledge or shipping a tab that cannot say it is the active one.
    protected override Component? Render() => null;
}
