using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's badge as a C# component: its examples and its reference.</summary>
[Route("badge")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiBadgePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Badge.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Badge)
            .Lead("A status, a category or a count, in any Tailwind colour. A badge is a <div> until it is told to "
                  + "be a button, and a close button inside it makes it removable.")
            .Files(["UiKitBadgeDemo.cs"])
            .Api(UiApi.Badge)
            .Demo(UiKitBadgeDemo)
            .Notes("The pressed amount and the removable roles are fields on the demo: a press runs C#, and the "
                   + "live diff redraws the badge.");
}
