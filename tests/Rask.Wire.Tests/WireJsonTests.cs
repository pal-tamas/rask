using System.Text;
using System.Text.Json;

namespace Rask.Wire.Tests;

// WireJson is what every generated codec is built from, so a mistake here is a mistake in every message
// at once. The round-trips below pin the wire format itself: changing one of them changes what a
// released client and a released server agree on.
public sealed class WireJsonTests
{
    [Fact]
    public void Scalars_round_trip_through_the_formats_the_wire_pins()
    {
        Assert.True(Read("true", static (ref Utf8JsonReader r) => WireJson.ReadBoolean(ref r, "p")));
        Assert.Equal("hi", Read("\"hi\"", static (ref Utf8JsonReader r) => WireJson.ReadString(ref r, "p")));
        Assert.Equal('x', Read("\"x\"", static (ref Utf8JsonReader r) => WireJson.ReadChar(ref r, "p")));
        Assert.Equal(42, Read("42", static (ref Utf8JsonReader r) => WireJson.ReadInt32(ref r, "p")));
        Assert.Equal(-7L, Read("-7", static (ref Utf8JsonReader r) => WireJson.ReadInt64(ref r, "p")));
        Assert.Equal(1.5, Read("1.5", static (ref Utf8JsonReader r) => WireJson.ReadDouble(ref r, "p")));
        Assert.Equal(2.25m, Read("2.25", static (ref Utf8JsonReader r) => WireJson.ReadDecimal(ref r, "p")));
        Assert.Equal(
            Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            Read("\"6f9619ff-8b86-d011-b42d-00c04fc964ff\"", static (ref Utf8JsonReader r) => WireJson.ReadGuid(ref r, "p")));
        Assert.Equal(
            new DateOnly(2026, 8, 18),
            Read("\"2026-08-18\"", static (ref Utf8JsonReader r) => WireJson.ReadDateOnly(ref r, "p")));
        Assert.Equal(
            new TimeSpan(1, 2, 3, 4),
            Read("\"1.02:03:04\"", static (ref Utf8JsonReader r) => WireJson.ReadTimeSpan(ref r, "p")));
        Assert.Equal(
            "abc"u8.ToArray(),
            Read("\"YWJj\"", static (ref Utf8JsonReader r) => WireJson.ReadBytes(ref r, "p")));
    }

    [Fact]
    public void The_writers_emit_exactly_what_the_readers_expect()
    {
        Assert.Equal("""{"p":"x"}""", Write(static w => WireJson.WriteCharValue(w, 'x')));
        Assert.Equal("""{"p":"1.02:03:04"}""", Write(static w => WireJson.WriteTimeSpanValue(w, new TimeSpan(1, 2, 3, 4))));
        Assert.Equal("""{"p":"2026-08-18"}""", Write(static w => WireJson.WriteDateOnlyValue(w, new DateOnly(2026, 8, 18))));
        Assert.Equal("""{"p":"09:30:00.0000000"}""", Write(static w => WireJson.WriteTimeOnlyValue(w, new TimeOnly(9, 30))));
        Assert.Equal("""{"p":"/a/b?c=1"}""", Write(static w => WireJson.WriteUriValue(w, new Uri("/a/b?c=1", UriKind.Relative))));
        Assert.Equal("""{"p":null}""", Write(static w => WireJson.WriteUriValue(w, null)));
    }

    [Fact]
    public void A_TimeSpan_survives_the_round_trip_it_is_written_for()
    {
        var original = new TimeSpan(5, 4, 3, 2, 1);
        var json = Write(w => WireJson.WriteTimeSpanValue(w, original));

        Assert.Equal(original, ReadProperty(json, static (ref Utf8JsonReader r) => WireJson.ReadTimeSpan(ref r, "p")));
    }

    [Fact]
    public void Null_reaches_a_nullable_reader_and_is_refused_by_a_required_one()
    {
        Assert.Null(Read("null", static (ref Utf8JsonReader r) => WireJson.ReadString(ref r, "p")));
        Assert.Null(Read("null", static (ref Utf8JsonReader r) => WireJson.ReadBytes(ref r, "p")));
        Assert.Null(Read("null", static (ref Utf8JsonReader r) => WireJson.ReadUri(ref r, "p")));

        var error = Assert.Throws<JsonException>(() =>
            Read("null", static (ref Utf8JsonReader r) => WireJson.ReadRequiredString(ref r, "p")));

        Assert.Contains("non-nullable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_token_names_the_property_and_what_was_expected()
    {
        var error = Assert.Throws<JsonException>(() =>
            Read("\"nope\"", static (ref Utf8JsonReader r) => WireJson.ReadInt32(ref r, "quantity")));

        Assert.Contains("quantity", error.Message, StringComparison.Ordinal);
        Assert.Contains("an int", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_number_out_of_range_says_so_rather_than_expected_a_number_got_a_number()
    {
        var error = Assert.Throws<JsonException>(() =>
            Read("99999", static (ref Utf8JsonReader r) => WireJson.ReadByte(ref r, "count")));

        Assert.Contains("outside the range", error.Message, StringComparison.Ordinal);
        Assert.Contains("count", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_multi_character_string_is_not_a_char()
    {
        var error = Assert.Throws<JsonException>(() =>
            Read("\"xy\"", static (ref Utf8JsonReader r) => WireJson.ReadChar(ref r, "initial")));

        Assert.Contains("one-character", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unparseable_date_or_duration_is_reported_against_its_property()
    {
        Assert.Contains("yyyy-MM-dd", Assert.Throws<JsonException>(() =>
            Read("\"18/08/2026\"", static (ref Utf8JsonReader r) => WireJson.ReadDateOnly(ref r, "p"))).Message,
            StringComparison.Ordinal);

        Assert.Contains("duration", Assert.Throws<JsonException>(() =>
            Read("\"soon\"", static (ref Utf8JsonReader r) => WireJson.ReadTimeSpan(ref r, "p"))).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveFile_maps_an_index_to_its_part_and_reports_a_truncated_body()
    {
        var file = RemoteFile.FromBytes("a.txt", null, [1]);

        Assert.Same(file, WireJson.ResolveFile([file], 0, "p"));
        Assert.Null(WireJson.ResolveFile([file], -1, "p"));

        var error = Assert.Throws<JsonException>(() => WireJson.ResolveFile([file], 3, "avatar"));
        Assert.Contains("avatar", error.Message, StringComparison.Ordinal);
        Assert.Contains("incomplete", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SkipValue_steps_over_a_property_the_receiver_does_not_know()
    {
        // The compatibility rule in one test: a sender that adds a property must not break a receiver
        // compiled before it existed.
        var json = """{"known":1,"added":{"nested":[1,2,3]},"after":2}"""u8.ToArray();
        var reader = new Utf8JsonReader(json);

        reader.Read();
        var seen = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            reader.Read();
            if (name == "added")
            {
                WireJson.SkipValue(ref reader);
                continue;
            }

            seen.Add($"{name}={WireJson.ReadInt32(ref reader, name)}");
        }

        Assert.Equal(["known=1", "after=2"], seen);
    }

    [Fact]
    public void The_shape_guards_reject_a_scalar_where_a_structure_belongs()
    {
        Assert.Throws<JsonException>(() =>
            Read("5", static (ref Utf8JsonReader r) =>
            {
                WireJson.ExpectStartObject(ref r, "p");
                return 0;
            }));

        Assert.Throws<JsonException>(() =>
            Read("5", static (ref Utf8JsonReader r) =>
            {
                WireJson.ExpectStartArray(ref r, "p");
                return 0;
            }));
    }

    private delegate T ReadValue<out T>(ref Utf8JsonReader reader);

    // Positions a reader on a bare JSON value, the state a codec's scalar readers are always called in.
    private static T Read<T>(string json, ReadValue<T> read)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return read(ref reader);
    }

    // Positions a reader on the value of the single property "p" of an object.
    private static T ReadProperty<T>(string json, ReadValue<T> read)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        reader.Read();
        reader.Read();
        return read(ref reader);
    }

    private static string Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("p");
            write(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
