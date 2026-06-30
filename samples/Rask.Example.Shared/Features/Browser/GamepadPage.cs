using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="GamepadDemo" /> (<c>IGamepad</c>).</summary>
[Route("browser/gamepad")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class GamepadPage : Component
{
    protected override RenderResult Head => Title()["Gamepad — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Gamepad",
            "Read connected game controllers — sticks, triggers, and buttons — via IGamepad (the Gamepad "
            + "API). The API has no input event, so the framework polls on requestAnimationFrame and pushes "
            + "a reading only when a pad's state changes. Works on both transports (over Server each reading "
            + "is a WebSocket round-trip, so prefer WASM for twitch input)."),
        CodeSample(
            ["GamepadDemo.cs"],
            Notes: "WatchAsync(onReading) returns a disposable subscription; the handler fires with a "
                + "GamepadReading per connected pad whenever its axes/buttons change. A pad only appears after "
                + "the user presses a button on it (a privacy gesture).",
            Result: GamepadDemo())
    ];
}
