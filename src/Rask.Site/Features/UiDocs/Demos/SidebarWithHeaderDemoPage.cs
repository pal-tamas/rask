using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux's sidebar layout with a secondary header as a whole page: what the sidebar page frames and its Fullscreen opens.</summary>
[Route("demo/sidebar-with-header")]
public sealed partial class SidebarWithHeaderDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Meta("Sidebar with a secondary header", UiDocsCatalog.Sidebar);

    /// <inheritdoc />
    protected override Component? Render() => SidebarSecondaryHeaderExample;
}
