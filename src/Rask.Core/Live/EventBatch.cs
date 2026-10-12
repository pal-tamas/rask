using System.Text.Json;

namespace Rask.Core.Live;

/// <summary>
///     What both hosts read off a <c>batch</c> frame — the events one browser task produced, sent together
///     (<c>rask-batch.ts</c>).
/// </summary>
internal static class EventBatch
{
    /// <summary>The most events one batch carries: what the client sends at most, the rest going in a frame of their own.</summary>
    internal const int MaxEvents = 256;

    /// <summary>The handler an entry of a batch names, or null for anything that is not an event for one.</summary>
    internal static string? HandlerIdOf(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object
        && entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;

    /// <summary>How many of a batch's entries are events for a handler.</summary>
    internal static int CountEvents(JsonElement events) =>
        events.EnumerateArray().Count(static entry => HandlerIdOf(entry) is not null);
}
