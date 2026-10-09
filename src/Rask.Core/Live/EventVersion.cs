using System.Text.Json;

namespace Rask.Core.Live;

/// <summary>
///     The page an event says it was read from: its <c>v</c>, the number the last page the browser applied carried
///     (zero for the document itself).
/// </summary>
internal static class EventVersion
{
    /// <summary>The name both the event and the page carry it under.</summary>
    internal const string Name = "v";

    /// <summary>
    ///     Reads it. False when the event carries one that is not a whole number from zero up — such an event names
    ///     no page that ever was. An event that carries none is fine, and <paramref name="version" /> is null.
    /// </summary>
    internal static bool TryRead(JsonElement payload, out int? version)
    {
        version = null;
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(Name, out var v))
        {
            return true;
        }

        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var number) || number < 0)
        {
            return false;
        }

        version = number;
        return true;
    }
}
