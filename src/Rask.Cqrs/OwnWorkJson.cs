using System.Text.Json;

namespace Rask.Background;

/// <summary>
///     The JSON <see cref="ReadOwnWork" />, <see cref="WatchOwnWork" /> and what they answer cross the wire as — written by
///     hand, because the generator that writes every other codec leaves Rask.Cqrs's own messages alone.
/// </summary>
/// <remarks>
///     <para>
///         A <see cref="WorkReported" /> crosses as what its reader sees and nothing else: the seven positional fields, its
///         <see cref="WorkReported.Batch" /> and <see cref="WorkReported.Detail" />, whether it is shared and — when the board
///         read it for this reader — whether they started it. Who it is for, the tenant, the lane and the job's kind stay on
///         the server, and a failure is <see cref="WorkState.Failed" /> without a word of the error it threw.
///     </para>
///     <para>
///         Both ends are tolerant across a deploy. <see cref="WorkState.Warned" /> crosses as <c>"state": "Done"</c> with
///         <c>"outcome": "Warned"</c> beside it, so a browser tab opened before it existed reads an end it knows; and this reader
///         skips a field it does not know and reads a state it does not know as <see cref="WorkState.Done" />, so the next
///         state added breaks no tab either.
///     </para>
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
        writer.WriteString("id", work.Id);
        writer.WriteString("title", work.Title);
        writer.WriteString("progress", work.Progress);
        WriteCount(writer, "done", work.Done);
        WriteCount(writer, "of", work.Of);
        // A state a reader from before it knows nothing of crosses as the one it can read — Warned as Done — and itself
        // beside it, so a tab opened before the deploy that added it shows the work ended rather than failing to read it.
        var legacy = work.State is WorkState.Warned ? WorkState.Done : work.State;
        writer.WriteString("state", legacy.ToString());
        if (legacy != work.State)
        {
            writer.WriteString("outcome", work.State.ToString());
        }

        if (work.Batch is { } batch)
        {
            writer.WriteString("batch", batch);
        }

        if (work.Detail is { } detail)
        {
            writer.WriteString("detail", detail);
        }

        if (work.Shared)
        {
            writer.WriteBoolean("shared", value: true);
        }

        if (work.Mine is { } mine)
        {
            writer.WriteBoolean("mine", mine);
        }

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

    // A job's id is a Guid on the wire; a number is a board from before Rask made its own keys, read as malformed.
    private static Guid IdOf(JsonElement id) =>
        id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var value)
            ? value
            : throw new JsonException("The work's id is not a Guid.");

    private static WorkReported Work(JsonElement element)
    {
        var work = Object(element);
        return new WorkReported(
            IdOf(Required(work, "id")),
            Text(Required(work, "title")) ?? "",
            work.TryGetProperty("progress", out var progress) ? Text(progress) : null,
            Count(work, "done"),
            Count(work, "of"),
            State(work.TryGetProperty("outcome", out var outcome) ? Text(outcome) : Text(Required(work, "state"))),
            Required(work, "at").GetDateTime().ToUniversalTime())
        {
            Batch = work.TryGetProperty("batch", out var batch) && batch.ValueKind == JsonValueKind.String ? batch.GetGuid() : null,
            Detail = work.TryGetProperty("detail", out var detail) ? Text(detail) : null,
            Shared = work.TryGetProperty("shared", out var shared) && shared.ValueKind == JsonValueKind.True,
            Mine = work.TryGetProperty("mine", out var mine) && mine.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? mine.GetBoolean()
                : null,
        };
    }

    // A state a newer server knows and this reader does not is an end it cannot name: shown as done, never a broken bell.
    // Only a name is a state — Enum.TryParse would take "7" too.
    private static WorkState State(string? state) =>
        state is { Length: > 0 } && char.IsAsciiLetter(state[0])
        && Enum.TryParse<WorkState>(state, ignoreCase: false, out var where) && Enum.IsDefined(where)
            ? where
            : WorkState.Done;

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
