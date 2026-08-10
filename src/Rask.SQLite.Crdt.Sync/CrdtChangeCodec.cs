using System.Buffers;
using System.Text.Json;

namespace Rask.SQLite.Crdt.Sync;

/// <summary>
///     Turns a batch of changes into the bytes of one object, and back.
/// </summary>
/// <remarks>
///     <para>
///         Written by hand against <see cref="Utf8JsonWriter" /> rather than through serialization
///         attributes, for two reasons: no reflection, so the package survives trimming and AOT; and a
///         change's value is <b>dynamically typed</b> — SQLite gives back a long, a double, a string, a
///         blob or null — so the type has to travel with the value. A value written back as the wrong
///         storage class is not a formatting difference, it is a different value, and it would surface as
///         a peer's row quietly changing type.
///     </para>
///     <para>
///         The envelope carries a version so a later format can be recognised rather than
///         mis-parsed — an object written today may still be read years from now by a device that has
///         been offline since.
///     </para>
/// </remarks>
internal static class CrdtChangeCodec
{
    private const int Version = 1;

    public static byte[] Encode(IReadOnlyList<CrdtChange> changes)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteStartArray("changes");

            foreach (var change in changes)
            {
                writer.WriteStartObject();
                writer.WriteString("table", change.Table);
                writer.WriteBase64String("pk", change.PrimaryKey);
                writer.WriteString("cid", change.ColumnName);
                WriteValue(writer, change.Value);
                writer.WriteNumber("cv", change.ColumnVersion);
                writer.WriteNumber("dv", change.DbVersion);
                writer.WriteBase64String("site", change.SiteId);
                writer.WriteNumber("cl", change.CausalLength);
                writer.WriteNumber("seq", change.Sequence);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static IReadOnlyList<CrdtChange> Decode(byte[] content)
    {
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        if (!root.TryGetProperty("v", out var version) || version.GetInt32() != Version)
        {
            throw new InvalidOperationException(
                $"Unsupported change-feed format '{(version.ValueKind == JsonValueKind.Number ? version.GetInt32() : -1)}'. " +
                $"This build reads version {Version} — a newer peer has written objects it cannot read.");
        }

        var changes = new List<CrdtChange>();
        foreach (var element in root.GetProperty("changes").EnumerateArray())
        {
            changes.Add(new CrdtChange(
                element.GetProperty("table").GetString()!,
                element.GetProperty("pk").GetBytesFromBase64(),
                element.GetProperty("cid").GetString()!,
                ReadValue(element),
                element.GetProperty("cv").GetInt64(),
                element.GetProperty("dv").GetInt64(),
                element.GetProperty("site").GetBytesFromBase64(),
                element.GetProperty("cl").GetInt64(),
                element.GetProperty("seq").GetInt64()));
        }

        return changes;
    }

    /// <summary>Writes the value tagged with its SQLite storage class, or a bare null.</summary>
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                writer.WriteNull("val");
                break;
            case long l:
                writer.WriteString("valt", "i");
                writer.WriteNumber("val", l);
                break;
            case int i:
                writer.WriteString("valt", "i");
                writer.WriteNumber("val", i);
                break;
            case double d:
                writer.WriteString("valt", "d");
                writer.WriteNumber("val", d);
                break;
            case string s:
                writer.WriteString("valt", "s");
                writer.WriteString("val", s);
                break;
            case byte[] b:
                writer.WriteString("valt", "b");
                writer.WriteBase64String("val", b);
                break;
            default:
                // Every value reaching here came out of a SQLite reader, which yields exactly the five
                // storage classes above. Anything else means the feed's shape changed underneath us, and
                // guessing an encoding would corrupt a peer's data rather than fail here.
                throw new InvalidOperationException(
                    $"A change carried a value of type {value.GetType().Name}, which is not one of " +
                    "SQLite's storage classes (integer, real, text, blob, null).");
        }
    }

    private static object? ReadValue(JsonElement element)
    {
        var value = element.GetProperty("val");
        if (value.ValueKind is JsonValueKind.Null)
        {
            return null;
        }

        return element.GetProperty("valt").GetString() switch
        {
            "i" => value.GetInt64(),
            "d" => value.GetDouble(),
            "s" => value.GetString(),
            "b" => value.GetBytesFromBase64(),
            var tag => throw new InvalidOperationException($"Unknown value tag '{tag}' in the change feed."),
        };
    }
}
