using System.Text.Json;

namespace Rask.Core.Live;

// Typed payload for the `wheel` event. Composes the MouseEventArgs geometry (access via
// <see cref="Mouse" />) and adds the scroll deltas. DeltaMode is 0 (pixels), 1 (lines) or 2 (pages).
public sealed record WheelEventArgs(
    MouseEventArgs Mouse,
    double DeltaX,
    double DeltaY,
    double DeltaZ,
    int DeltaMode)
{
    internal static WheelEventArgs FromJson(JsonElement p) => new(
        MouseEventArgs.FromJson(p),
        EventPayload.ReadDouble(p, "deltaX"),
        EventPayload.ReadDouble(p, "deltaY"),
        EventPayload.ReadDouble(p, "deltaZ"),
        EventPayload.ReadInt(p, "deltaMode"));
}
