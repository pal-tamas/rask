using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Sync;

/// <summary>
///     One entry in the log: what a single row's fields were set to, or that the row was deleted, stamped
///     with when it happened on the clock that issued it.
/// </summary>
/// <remarks>
///     <para>
///         An op carries the fields that <b>changed</b>, not the whole row. That is what lets two devices
///         edit different fields of the same record while offline and both keep their edit — a whole-row
///         op would silently discard one of them.
///     </para>
///     <para>
///         Values are held as <b>raw JSON text</b> (<c>true</c>, <c>"Buy milk"</c>, <c>null</c>, an object,
///         an array). The engine never interprets them: it compares and replaces them whole, so it needs no
///         knowledge of the application's types and stays usable from any layer. Keeping them as text rather
///         than as <see cref="JsonElement" /> also means an op owns its own data and has value equality,
///         which duplicate detection depends on.
///     </para>
///     <para>Ops are immutable, self-describing and safe to apply more than once, in any order.</para>
/// </remarks>
public sealed record SyncOp
{
    /// <summary>The entity type this row belongs to, e.g. <c>"Todo"</c>.</summary>
    [JsonPropertyName("e")]
    public required string Entity { get; init; }

    /// <summary>
    ///     The row's key. A <see cref="Guid" /> because an offline insert has to mint its own key: a
    ///     database-assigned identity cannot be issued without a round trip, and two devices inserting
    ///     offline would collide.
    /// </summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>When this happened, on the issuing node's hybrid logical clock.</summary>
    [JsonPropertyName("t")]
    public required HlcTimestamp Stamp { get; init; }

    /// <summary>
    ///     Field name to raw JSON value, for the fields this op changed. <c>null</c> on a delete.
    /// </summary>
    [JsonPropertyName("set")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(RawJsonValuesConverter))]
    public IReadOnlyDictionary<string, string>? Set { get; init; }

    /// <summary>Whether this op deletes the row. A delete carries no <see cref="Set" />.</summary>
    [JsonPropertyName("d")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Deleted { get; init; }

    /// <summary>Creates an op that sets <paramref name="values" /> on a row.</summary>
    public static SyncOp SetFields(
        string entity, Guid id, HlcTimestamp stamp, IReadOnlyDictionary<string, string> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentNullException.ThrowIfNull(values);

        return new SyncOp { Entity = entity, Id = id, Stamp = stamp, Set = values };
    }

    /// <summary>Creates an op that deletes a row.</summary>
    public static SyncOp Delete(string entity, Guid id, HlcTimestamp stamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);

        return new SyncOp { Entity = entity, Id = id, Stamp = stamp, Deleted = true };
    }
}

/// <summary>
///     Serialises <see cref="HlcTimestamp" /> as its sortable string form rather than as an object, so a
///     log stays readable and a stamp can be compared as text.
/// </summary>
public sealed class HlcTimestampConverter : JsonConverter<HlcTimestamp>
{
    /// <inheritdoc />
    public override HlcTimestamp Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        HlcTimestamp.TryParse(reader.GetString(), out var stamp)
            ? stamp
            : throw new JsonException("Expected a hybrid logical clock timestamp.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, HlcTimestamp value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>
///     Writes the <see cref="SyncOp.Set" /> values through verbatim instead of quoting them, so
///     <c>{"done":true}</c> stays <c>{"done":true}</c> rather than becoming <c>{"done":"true"}</c>.
/// </summary>
/// <remarks>
///     The dictionary holds raw JSON text, which is what keeps the engine type-agnostic. Without this the
///     round trip would double-encode on every hop and a log would become unreadable after two.
/// </remarks>
public sealed class RawJsonValuesConverter : JsonConverter<IReadOnlyDictionary<string, string>?>
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<string, string>? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object of field values.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            reader.Read();
            using var value = JsonDocument.ParseValue(ref reader);
            values[name] = value.RootElement.GetRawText();
        }

        return values;
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer, IReadOnlyDictionary<string, string>? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        foreach (var (name, raw) in value)
        {
            writer.WritePropertyName(name);
            writer.WriteRawValue(raw);
        }

        writer.WriteEndObject();
    }
}

/// <summary>Source-generated serialisation for the log, so it stays correct under trimming and AOT.</summary>
[JsonSourceGenerationOptions(Converters = [typeof(HlcTimestampConverter)])]
[JsonSerializable(typeof(SyncOp))]
[JsonSerializable(typeof(IReadOnlyList<SyncOp>))]
[JsonSerializable(typeof(SyncOp[]))]
public sealed partial class SyncJsonContext : JsonSerializerContext;
