using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="VibrationDemo" /> (<c>IVibration</c>).</summary>
[Route("browser/vibration")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class VibrationPage : Component
{
    protected override RenderResult Head => Title()["Vibration — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Vibration",
            "Pulse the device's vibration motor via IVibration (navigator.vibrate) — effective on mobile."),
        CodeSample(
            ["VibrationDemo.cs"],
            Notes: "A pattern is alternating vibrate/pause durations in ms. Returns false on devices that can't vibrate.",
            Result: VibrationDemo())
    ];
}
