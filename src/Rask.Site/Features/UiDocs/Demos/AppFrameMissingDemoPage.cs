using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     What the app frame demo shows at an address no page answers: the built-in 404 of an app on the kit, drawn here by
///     hand because the site's own not-found page wins over it.
/// </summary>
[Route("gone")]
[ParentRoute(typeof(Examples.AppFrameExample))]
public sealed partial class AppFrameMissingDemoPage : Component
{
    /// <inheritdoc />
    protected override Component? Render() => Div.Key("gone")[Ui.NotFound.Home(Routes.AppFrameHomeDemoPage())];
}
