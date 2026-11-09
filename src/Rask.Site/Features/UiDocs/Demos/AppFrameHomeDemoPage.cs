using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>The app frame demo's home: the row every other row is beneath, and the way to the other pages.</summary>
[Route("")]
[ParentRoute(typeof(Examples.AppFrameExample))]
public sealed partial class AppFrameHomeDemoPage : Component
{
    /// <inheritdoc />
    protected override string? PageTitle => "Home";

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Key("home").Class("space-y-4")[
            Ui.Heading.Level(1).Xl["Good afternoon, Mara"],
            Ui.Text["Open a list from the menu, then a record in it: the trail above leads back to the list as you left it."],
            Ui.Link.Href(Routes.AppFrameMissingDemoPage()).Attributes(("data-testid", "af-gone"))["A page that is gone"]
        ];
}
