using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="GeolocationDemo" /> (<c>IGeolocation</c>).</summary>
[Route("browser/geolocation")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class GeolocationPage : Component
{
    protected override RenderResult Head => Title()["Geolocation — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Geolocation",
            "One-shot device position via IGeolocation — the callback API wrapped in a Promise by the framework."),
        CodeSample(
            ["GeolocationDemo.cs"],
            Notes: "Requires a secure context + permission; getCurrentPosition is callback-based, so it goes through __raskApi.geolocation.",
            Result: GeolocationDemo())
    ];
}
