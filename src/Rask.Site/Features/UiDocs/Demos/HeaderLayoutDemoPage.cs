using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux's header layout as a whole page: what the header page frames and its Fullscreen opens.</summary>
[Route("demo/header")]
public sealed partial class HeaderLayoutDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiLayoutDemo.Meta("Header", UiDocsCatalog.Header);

    /// <inheritdoc />
    protected override Component? Render() => HeaderLayoutExample;
}
