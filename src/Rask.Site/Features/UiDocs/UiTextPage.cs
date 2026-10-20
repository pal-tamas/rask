using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's text and link as C# components: its examples and its reference.</summary>
[Route("text")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTextPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Text.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Text)
            .Lead("Body copy in three inks, four sizes or any Tailwind hue, and the link that sits in it. A link "
                  + "given a generated route stays inside the app; one given a URL is a link the browser follows.")
            .Files(["UiKitTextDemo.cs"])
            .Api(UiApi.Text)
            .Demo(UiKitTextDemo);
}
