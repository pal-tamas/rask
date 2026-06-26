using System.Text.Json;

namespace Rask.Core.Live;

// Typed payload for the Touch Events (touchstart/end/move/cancel). The client reports the number of
// active touch points plus the coordinates of the first touch (the common single-touch case) and the
// modifier flags. The full per-touch list is intentionally not marshalled — a server handler that
// needs richer multi-touch data should use the Pointer Events instead.
public sealed record TouchEventArgs(
    int TouchCount,
    double ClientX,
    double ClientY,
    double PageX,
    double PageY,
    bool Shift,
    bool Ctrl,
    bool Alt,
    bool Meta)
{
    internal static TouchEventArgs FromJson(JsonElement p) => new(
        EventPayload.ReadInt(p, "touchCount"),
        EventPayload.ReadDouble(p, "clientX"),
        EventPayload.ReadDouble(p, "clientY"),
        EventPayload.ReadDouble(p, "pageX"),
        EventPayload.ReadDouble(p, "pageY"),
        EventPayload.ReadBool(p, "shiftKey"),
        EventPayload.ReadBool(p, "ctrlKey"),
        EventPayload.ReadBool(p, "altKey"),
        EventPayload.ReadBool(p, "metaKey"));
}
