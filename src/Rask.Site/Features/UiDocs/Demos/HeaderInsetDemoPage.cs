using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux's header layout with an inset main as a whole page: what the header page frames and its Fullscreen opens.</summary>
[Route("demo/header-inset")]
public sealed partial class HeaderInsetDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Meta("Header with an inset main", UiDocsCatalog.Header);

    /// <inheritdoc />
    protected override Component? Render() => HeaderInsetExample;
}
