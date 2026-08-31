using Rask.Core.Routing;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The console's own not-found page.
/// </summary>
/// <remarks>
/// The console is a mounted application with its own route table, so a mistyped <c>/_rask</c> URL can no
/// longer fall through to the host application's <c>[NotFound]</c> — which would have rendered the host's
/// markup, inside the console's document, styled by the console's stylesheet.
/// <para>
/// Without this the fallback would be the framework's own built-in page, which is correct but arrives
/// OUTSIDE <see cref="DashboardLayout" /> and therefore without the stylesheet the layout contributes. So
/// this exists for the same reason every other page here has a parent route: the chrome is the layout's
/// job, and a page that renders without it looks broken rather than empty.
/// </para>
/// </remarks>
[NotFound]
[ParentRoute(typeof(DashboardLayout))]
public sealed partial class NotFoundPage : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        DashboardEmpty
            .Heading("No such page")
            .Detail("That URL is not part of the console. Pick a panel from the navigation above.");
}
