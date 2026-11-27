using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rask.Wire;

namespace Rask.Values;

/// <summary>
///     How System.Text.Json writes and reads a <see cref="Moment" />: as the UTC instant it holds, an ISO 8601 string with
///     its Z — <c>"2026-10-25T00:30:00Z"</c> — so the hour the clock shows twice in autumn survives the trip. The value
///     object generator puts it on each record declared from <see cref="Moment" />, through a nested subclass that makes
///     the record; not an API to write against.
/// </summary>
/// <remarks>
///     <para>
///         A string with a Z or an offset is the instant it names; one without is a wall-clock time of
///         <see cref="Clock.Zone" />. So is the object a moment was written as before it travelled as an instant —
///         <c>{"value":"2026-10-25T02:30:00","instant":"2026-10-25T02:30:00+02:00",…}</c> — read through its instant
///         when it has one, its value otherwise, in either casing. That old form is read for one release.
///     </para>
///     <para>
///         What is read is made inside a loading scope, as a row loaded from the database is: a rule tightened since the
///         moment was written must not make a stored job or document unreadable.
///     </para>
/// </remarks>
/// <typeparam name="TMoment">The moment.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class MomentJsonConverter<TMoment> : JsonConverter<TMoment>
    where TMoment : Moment
{
    /// <inheritdoc />
    public sealed override TMoment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        var held = reader.TokenType == JsonTokenType.StartObject
            ? FromObject(ref reader, typeToConvert.Name)
            : WireJson.ReadMoment(ref reader, typeToConvert.Name);

        using var loading = ValueLoading.Begin();
        return Make(held);
    }

    /// <inheritdoc />
    public sealed override void Write(Utf8JsonWriter writer, TMoment value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(value.Instant.UtcDateTime);
    }

    /// <summary>The moment, made from what was read: the instant, of <see cref="DateTimeKind.Utc" />, or a wall-clock time.</summary>
    /// <param name="moment">What was read.</param>
    /// <returns>The moment.</returns>
    protected abstract TMoment Make(DateTime moment);

    // The object form a moment was written as before: its instant when it carries one, else its wall-clock value.
    private static DateTime FromObject(ref Utf8JsonReader reader, string property)
    {
        DateTime? value = null;
        DateTime? instant = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isValue = reader.ValueTextEquals("Value"u8) || reader.ValueTextEquals("value"u8);
            var isInstant = reader.ValueTextEquals("Instant"u8) || reader.ValueTextEquals("instant"u8);
            reader.Read();
            if (isInstant)
            {
                instant = WireJson.ReadMoment(ref reader, property);
            }
            else if (isValue)
            {
                value = WireJson.ReadMoment(ref reader, property);
            }
            else
            {
                reader.Skip();
            }
        }

        return instant ?? value ?? throw new JsonException($"'{property}' holds no moment.");
    }
}
