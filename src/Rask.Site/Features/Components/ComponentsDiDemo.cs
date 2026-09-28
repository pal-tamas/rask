namespace Rask.Site.Features;

// Call site is unchanged — ActivatorUtilities resolves `http`:
public sealed partial class ComponentsDiDemo : Component
{
    protected override Component? Render() => WeatherCard.City("Helsinki");
}
