using System.Text.Json;

namespace Rask.Core.Live;

// Shared JSON-payload readers for the typed DOM event-arg records (MouseEventArgs, WheelEventArgs,
// PointerEventArgs, …). The client serialises each DOM event into a flat JSON object; these helpers
// pull individual fields out defensively (missing/wrong-typed fields fall back to a zero/empty
// default) so a record's FromJson stays a one-liner per field. Mirrors the inline readers that
// KeyboardEventArgs/ScrollEvent grew first; centralised here now that many records need them.
internal static class EventPayload
{
    public static string ReadString(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    public static bool ReadBool(JsonElement p, string name) =>
        p.ValueKind == JsonValueKind.Object && p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static int ReadInt(JsonElement p, string name)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty(name, out var v))
        {
            return 0;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.Number => (int)v.GetDouble(),
            JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
            _ => 0
        };
    }

    public static double ReadDouble(JsonElement p, string name)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty(name, out var v))
        {
            return 0;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetDouble(out var d) => d,
            JsonValueKind.String when double.TryParse(v.GetString(), out var d) => d,
            _ => 0
        };
    }
}
