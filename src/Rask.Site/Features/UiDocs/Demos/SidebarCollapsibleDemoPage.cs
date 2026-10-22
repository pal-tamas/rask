using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux's collapsible sidebar layout as a whole page: what the sidebar page frames and its Fullscreen opens.</summary>
[Route("demo/sidebar-collapsible")]
public sealed partial class SidebarCollapsibleDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Meta("Collapsible sidebar", UiDocsCatalog.Sidebar);

    /// <inheritdoc />
    protected override Component? Render() => SidebarCollapsibleExample;
}
