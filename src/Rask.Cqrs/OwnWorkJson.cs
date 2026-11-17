using System.Text.Json;

namespace Rask.Background;

/// <summary>
///     The JSON <see cref="ReadOwnWork" />, <see cref="WatchOwnWork" /> and what they answer cross the wire as — written by
///     hand, because the generator that writes every other codec leaves Rask.Cqrs's own messages alone.
/// </summary>
/// <remarks>
///     A <see cref="WorkReported" /> crosses as the seven things its enqueuer sees and nothing else: who it is for, the tenant
///     and the lane stay on the server, and a failure is <see cref="WorkState.Failed" /> without a word of why.
/// </remarks>
internal static class OwnWorkJson
{
    /// <summary>Writes what a <see cref="ReadOwnWork" /> or a <see cref="WatchOwnWork" /> asks for: one lane, or all.</summary>
    public static void WriteLane(Utf8JsonWriter writer, string? lane)
    {
        writer.WriteStartObject();
        writer.WriteString("lane", lane);
        writer.WriteEndObject();
    }

    /// <summary>Reads what <see cref="WriteLane" /> wrote.</summary>
    /// <exception cref="JsonException">It is not an object of an optional string lane.</exception>
    public static string? ReadLane(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = Object(document.RootElement);
        return root.TryGetProperty("lane", out var lane) ? Text(lane) : null;
    }

    /// <summary>Writes the server board's answer.</summary>
    public static void WriteRead(Utf8JsonWriter writer, OwnWorkRead read)
    {
        writer.WriteStartObject();
        writer.WriteNumber("every", (long)read.Every.TotalMilliseconds);
        writer.WriteStartArray("work");
        foreach (var work in read.Work)
        {
            WriteWork(writer, work);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>Reads what <see cref="WriteRead" /> wrote.</summary>
    /// <exception cref="JsonException">It is not the board's answer.</exception>
    public static OwnWorkRead ReadRead(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = Object(document.RootElement);
        var every = TimeSpan.FromMilliseconds(Required(root, "every").GetInt64());
        var rows = Required(root, "work");
        if (rows.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The board's work is not a list.");
        }

        return new OwnWorkRead(every, [.. rows.EnumerateArray().Select(Work)]);
    }

    /// <summary>Writes one piece of work as its enqueuer sees it.</summary>
    public static void WriteWork(Utf8JsonWriter writer, WorkReported work)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(work);
        writer.WriteStartObject();
        writer.WriteNumber("id", work.Id);
        writer.WriteString("title", work.Title);
        writer.WriteString("progress", work.Progress);
        WriteCount(writer, "done", work.Done);
        WriteCount(writer, "of", work.Of);
        writer.WriteString("state", work.State.ToString());

        // The server's clocks are UTC; a row read back from a store that keeps no kind says so here, once.
        writer.WriteString("at", work.At.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(work.At, DateTimeKind.Utc) : work.At);
        writer.WriteEndObject();
    }

    /// <summary>Reads what <see cref="WriteWork" /> wrote.</summary>
    /// <exception cref="JsonException">It is not a piece of work.</exception>
    public static WorkReported ReadWork(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Work(document.RootElement);
    }

    private static WorkReported Work(JsonElement element)
    {
        var work = Object(element);
        var state = Text(Required(work, "state"));
        if (!Enum.TryParse<WorkState>(state, ignoreCase: false, out var where) || !Enum.IsDefined(where))
        {
            throw new JsonException($"'{state}' is not where work can stand.");
        }

        return new WorkReported(
            Required(work, "id").GetInt64(),
            Text(Required(work, "title")) ?? "",
            work.TryGetProperty("progress", out var progress) ? Text(progress) : null,
            Count(work, "done"),
            Count(work, "of"),
            where,
            Required(work, "at").GetDateTime().ToUniversalTime());
    }

    private static void WriteCount(Utf8JsonWriter writer, string name, int? count)
    {
        if (count is { } value)
        {
            writer.WriteNumber(name, value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static int? Count(JsonElement work, string name) =>
        work.TryGetProperty(name, out var count) && count.ValueKind != JsonValueKind.Null ? count.GetInt32() : null;

    private static JsonElement Object(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? element : throw new JsonException("Expected a JSON object.");

    private static JsonElement Required(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : throw new JsonException($"'{name}' is missing.");

    private static string? Text(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        _ => throw new JsonException("Expected a string."),
    };
}
