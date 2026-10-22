using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux's sidebar layout with an inset main as a whole page: what the sidebar page frames and its Fullscreen opens.</summary>
[Route("demo/sidebar-inset")]
public sealed partial class SidebarInsetDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Meta("Sidebar with an inset main", UiDocsCatalog.Sidebar);

    /// <inheritdoc />
    protected override Component? Render() => SidebarInsetExample;
}
