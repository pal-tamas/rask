using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="GeolocationWatchDemo" /> (<c>IGeolocation.WatchAsync</c>).</summary>
[Route("browser/geolocation-watch")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class GeolocationWatchPage : Component
{
    protected override RenderResult Head => Title()["Live location — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Live location",
            "Track the device's position live via IGeolocation.WatchAsync (navigator.geolocation.watchPosition) "
            + "— for maps or fitness. The browser pushes each fix to C#, which re-renders. Dispose to stop. "
            + "Works on both transports; requires location permission."),
        CodeSample(
            ["GeolocationWatchDemo.cs"],
            Notes: "WatchAsync(handler, options?) returns an IAsyncDisposable and fires for the initial fix "
                + "plus each update; the handler is pushed from JS via a static [JSInvokable]. Pairs with the "
                + "one-shot GetCurrentPositionAsync.",
            Result: GeolocationWatchDemo())
    ];
}
