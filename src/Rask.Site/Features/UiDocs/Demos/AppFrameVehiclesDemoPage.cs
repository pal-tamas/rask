using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>The app frame demo's list in a group of the menu: its trail names the group.</summary>
[Route("vehicles")]
[ParentRoute(typeof(Examples.AppFrameExample))]
public sealed partial class AppFrameVehiclesDemoPage : Component
{
    /// <inheritdoc />
    protected override string? PageTitle => "Vehicles";

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Key("vehicles").Class("space-y-4")[
            Ui.Heading.Level(1).Xl["Vehicles"],
            Ui.Empty["No vehicles yet."]
        ];
}
