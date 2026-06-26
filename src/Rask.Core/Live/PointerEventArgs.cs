using System.Text.Json;

namespace Rask.Core.Live;

// Typed payload for the Pointer Events (pointerdown/up/move/over/out/enter/leave/cancel). Composes the
// MouseEventArgs geometry (access via <see cref="Mouse" />) and adds the pointer-device fields:
// PointerType is "mouse" | "pen" | "touch", Pressure is 0..1, IsPrimary marks the primary pointer in a
// multi-touch interaction.
public sealed record PointerEventArgs(
    MouseEventArgs Mouse,
    int PointerId,
    double Width,
    double Height,
    double Pressure,
    double TangentialPressure,
    double TiltX,
    double TiltY,
    double Twist,
    string PointerType,
    bool IsPrimary)
{
    internal static PointerEventArgs FromJson(JsonElement p) => new(
        MouseEventArgs.FromJson(p),
        EventPayload.ReadInt(p, "pointerId"),
        EventPayload.ReadDouble(p, "width"),
        EventPayload.ReadDouble(p, "height"),
        EventPayload.ReadDouble(p, "pressure"),
        EventPayload.ReadDouble(p, "tangentialPressure"),
        EventPayload.ReadDouble(p, "tiltX"),
        EventPayload.ReadDouble(p, "tiltY"),
        EventPayload.ReadDouble(p, "twist"),
        EventPayload.ReadString(p, "pointerType"),
        EventPayload.ReadBool(p, "isPrimary"));
}
