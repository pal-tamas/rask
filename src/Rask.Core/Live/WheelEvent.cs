using System.Text.Json;

namespace Rask.Core.Live;

// Typed payload for the `wheel` event. Composes the MouseEvent geometry (access via
// <see cref="Mouse" />) and adds the scroll deltas. DeltaMode is 0 (pixels), 1 (lines) or 2 (pages).
public sealed record WheelEvent(
    MouseEvent Mouse,
    double DeltaX,
    double DeltaY,
    double DeltaZ,
    int DeltaMode)
{
    internal static WheelEvent FromJson(JsonElement p) => new(
        MouseEvent.FromJson(p),
        EventPayload.ReadDouble(p, "deltaX"),
        EventPayload.ReadDouble(p, "deltaY"),
        EventPayload.ReadDouble(p, "deltaZ"),
        EventPayload.ReadInt(p, "deltaMode"));
}
