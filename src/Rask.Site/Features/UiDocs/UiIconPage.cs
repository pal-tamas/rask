using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Heroicons as a C# component: its examples and its reference.</summary>
[Route("icon")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiIconPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Icon.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Icon)
            .Lead("Every Heroicon, by name, as an inline SVG: outline, solid, mini and micro, sized and coloured "
                  + "with a class. Most of the kit takes an icon as a step and places it itself.")
            .Files(["UiKitIconDemo.cs"])
            .Api(UiApi.Icon)
            .Demo(UiKitIconDemo);
}
