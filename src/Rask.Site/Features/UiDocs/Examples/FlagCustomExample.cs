namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class FlagCustomExample : Component
{
    // A flag of the page's own: Src takes any image.
    private const string Pennant =
        "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 3 2'%3E%3Cpath fill='%23fff' d='M0 0h3v2H0z'/%3E"
        + "%3Cpath fill='%23be123c' d='M0 1.6h3V2H0z'/%3E%3Ccircle cx='.6' cy='.6' r='.3' fill='%23be123c'/%3E%3C/svg%3E";

    protected override Component? Render() => Ui.Flag.Src(Pennant).Alt("Pennant").Xl;
}
