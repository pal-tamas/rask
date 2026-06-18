using System.Text.Json;

namespace Rask.Core.Live;

// The typed payload for OnKeyDown/OnKeyUp on Element. The client serialises the parts of the DOM
// KeyboardEvent a server-side (or WASM) handler can act on without a live event object: the logical
// Key ("Escape", "Enter", "a"), the physical Code ("KeyA"), the four modifier flags, and Repeat
// (true while a key auto-repeats). Mirrors ScrollEvent's self-contained FromJson so the dispatcher
// in Component.TryInvokeHandlerAsync can unpack the WS/JS payload into one record.
public sealed record KeyboardEventArgs(
    string Key,
    string Code,
    bool Shift,
    bool Ctrl,
    bool Alt,
    bool Meta,
    bool Repeat)
{
    internal static KeyboardEventArgs FromJson(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new KeyboardEventArgs("", "", false, false, false, false, false);
        }

        return new KeyboardEventArgs(
            ReadString(payload, "key"),
            ReadString(payload, "code"),
            ReadBool(payload, "shiftKey"),
            ReadBool(payload, "ctrlKey"),
            ReadBool(payload, "altKey"),
            ReadBool(payload, "metaKey"),
            ReadBool(payload, "repeat"));
    }

    private static string ReadString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static bool ReadBool(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;
}
