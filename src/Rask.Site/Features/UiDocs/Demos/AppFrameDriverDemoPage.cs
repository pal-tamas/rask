using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>The app frame demo's page beneath its list: one driver, its name the trail's last step.</summary>
[Route("drivers/{Id}")]
[ParentRoute(typeof(Examples.AppFrameExample))]
public sealed partial class AppFrameDriverDemoPage : Component
{
    /// <summary>Which driver.</summary>
    [RouteParam]
    public string Id { get; set; } = "";

    private AppFrameDemoDriver? Driver => AppFrameDemoDrivers.All.FirstOrDefault(driver => string.Equals(driver.Id, Id, StringComparison.Ordinal));

    /// <inheritdoc />
    protected override string? PageTitle => Driver?.Name;

    /// <inheritdoc />
    protected override Component? Render() =>
        Driver is { } driver ? Details(driver) : Ui.NotFound.Home(Routes.AppFrameHomeDemoPage());

    private static Component Details(AppFrameDemoDriver driver) =>
        Div.Key("driver").Class("space-y-4")[
            Ui.Heading.Level(1).Xl[driver.Name],
            Ui.Text["Licence " + driver.Licence],
            driver.Active ? Ui.Badge.Color(Ui.Color.Lime)["Driving this week"] : null
        ];
}
