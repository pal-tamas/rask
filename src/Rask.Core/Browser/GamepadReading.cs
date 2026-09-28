namespace Rask.Core.Browser;

/// <summary>A snapshot of one connected gamepad (a <c>Gamepad</c> of the Gamepad API).</summary>
/// <param name="Index">The pad's slot in <c>navigator.getGamepads()</c> (stable for the connection).</param>
/// <param name="Id">The device identification string (controller make/model).</param>
/// <param name="Connected">Whether the pad is currently connected (<c>false</c> on the disconnect reading).</param>
/// <param name="Axes">Analog stick/trigger axes, each <c>-1</c>–<c>1</c> (sticks) — order is device-defined.</param>
/// <param name="Buttons">Per-button analog values, each <c>0</c>–<c>1</c> (<c>1</c> = fully pressed).</param>
public sealed record GamepadReading(
    int Index,
    string Id,
    bool Connected,
    IReadOnlyList<double> Axes,
    IReadOnlyList<double> Buttons);
