using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux's sidebar layout as a whole page: what the sidebar page frames and its Fullscreen opens.</summary>
[Route("demo/sidebar")]
public sealed partial class SidebarLayoutDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Meta("Sidebar", UiDocsCatalog.Sidebar);

    /// <inheritdoc />
    protected override Component? Render() => SidebarLayoutExample;
}
