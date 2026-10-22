using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux's header layout with a secondary sidebar as a whole page: what the header page frames and its Fullscreen opens.</summary>
[Route("demo/header-with-sidebar")]
public sealed partial class HeaderWithSidebarDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Meta("Header with a secondary sidebar", UiDocsCatalog.Header);

    /// <inheritdoc />
    protected override Component? Render() => HeaderSecondarySidebarExample;
}
