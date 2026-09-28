using System.Globalization;
using System.Text.Json;

namespace Rask.Core.Live;

// Shared JSON-payload readers for the typed DOM event-arg records (MouseEvent, WheelEvent,
// PointerEvent, …). The client serialises each DOM event into a flat JSON object; these helpers
// pull individual fields out defensively (missing/wrong-typed fields fall back to a zero/empty
// default) so a record's FromJson stays a one-liner per field. Mirrors the inline readers that
// KeyboardEvent/Event grew first; centralised here now that many records need them.
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
            // Invariant: the client serialises DOM numbers with JS number formatting, which is
            // invariant. Parsing them under an ambient culture would read "1.5" as 15 in de-DE.
            JsonValueKind.String when int.TryParse(
                v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
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
            // Invariant, for the reason given in ReadInt above — these carry wheel/pointer deltas.
            JsonValueKind.String when double.TryParse(
                v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0
        };
    }
    public static long ReadLong(JsonElement p, string name) =>
        p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    // A list of snapshots (a TouchList's Touch entries), each read by `read`; empty when the field is absent.
    public static IReadOnlyList<T> ReadList<T>(JsonElement p, string name, Func<JsonElement, T> read)
    {
        if (!p.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<T>(v.GetArrayLength());
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                list.Add(read(item));
            }
        }

        return list;
    }

    public static IReadOnlyList<string> ReadStrings(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<string>(v.GetArrayLength());
        foreach (var item in v.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                list.Add(item.GetString()!);
            }
        }

        return list;
    }

    // A string-to-string object (a DataTransfer's data by format); null when the field is absent.
    public static IReadOnlyDictionary<string, string>? ReadMap(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in v.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                map[entry.Name] = entry.Value.GetString()!;
            }
        }

        return map;
    }
}
