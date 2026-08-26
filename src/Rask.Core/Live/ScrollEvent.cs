using System.Globalization;
using System.Text.Json;

namespace Rask.Core.Live;

public sealed record ScrollEvent(int ScrollTop, int ClientHeight, int ScrollHeight)
{
    internal static ScrollEvent FromJson(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return new ScrollEvent(0, 0, 0);
        }

        return new ScrollEvent(
            ReadInt(payload, "scrollTop"),
            ReadInt(payload, "clientHeight"),
            ReadInt(payload, "scrollHeight"));
    }

    private static int ReadInt(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var v))
        {
            return 0;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.Number => (int)v.GetDouble(),
            // Invariant: scroll offsets cross the wire as JS-formatted numbers.
            JsonValueKind.String when int.TryParse(
                v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => 0
        };
    }
}
