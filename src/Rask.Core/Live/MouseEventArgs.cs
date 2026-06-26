using System.Text.Json;

namespace Rask.Core.Live;

// Typed payload for the pointer-position DOM events (click, dblclick, contextmenu, mousedown/up/move,
// mouseenter/leave, mouseover/out). The client serialises the geometry + button state + modifier flags
// of the DOM MouseEvent so a server-side or WASM handler can act on them without a live event object.
// WheelEventArgs and PointerEventArgs compose this record and add their own fields.
public sealed record MouseEventArgs(
    int Button,
    int Buttons,
    double ClientX,
    double ClientY,
    double ScreenX,
    double ScreenY,
    double PageX,
    double PageY,
    double OffsetX,
    double OffsetY,
    double MovementX,
    double MovementY,
    bool Shift,
    bool Ctrl,
    bool Alt,
    bool Meta)
{
    internal static MouseEventArgs FromJson(JsonElement p) => new(
        EventPayload.ReadInt(p, "button"),
        EventPayload.ReadInt(p, "buttons"),
        EventPayload.ReadDouble(p, "clientX"),
        EventPayload.ReadDouble(p, "clientY"),
        EventPayload.ReadDouble(p, "screenX"),
        EventPayload.ReadDouble(p, "screenY"),
        EventPayload.ReadDouble(p, "pageX"),
        EventPayload.ReadDouble(p, "pageY"),
        EventPayload.ReadDouble(p, "offsetX"),
        EventPayload.ReadDouble(p, "offsetY"),
        EventPayload.ReadDouble(p, "movementX"),
        EventPayload.ReadDouble(p, "movementY"),
        EventPayload.ReadBool(p, "shiftKey"),
        EventPayload.ReadBool(p, "ctrlKey"),
        EventPayload.ReadBool(p, "altKey"),
        EventPayload.ReadBool(p, "metaKey"));
}
