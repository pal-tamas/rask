using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's separator as a C# component: its examples and its reference.</summary>
[Route("separator")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiSeparatorPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Separator.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Separator)
            .Lead("A line between sections of content or groups of items. It carries no margin — the page spaces "
                  + "it — and a vertical one is as tall as its row until a vertical margin shortens it.")
            .Files(["UiKitSeparatorDemo.cs"])
            .Api(UiApi.Separator)
            .Demo(UiKitSeparatorDemo);
}
