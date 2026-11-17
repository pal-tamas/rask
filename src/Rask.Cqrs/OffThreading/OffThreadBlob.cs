using System.Text.Json;
using Rask.Wire;

namespace Rask.Cqrs;

// One blob of a frame, read as the file a codec asked to carry: a handler's IRaskFile, or a byte[] the codec moved
// beside the JSON. A view over the frame, so the bytes are copied once — into whatever the codec builds of them.
internal sealed class OffThreadBlob(OffThreadBlob.Shape shape, byte[] frame, int offset) : RemoteFile
{
    public override string Name => shape.Name;

    public override string ContentType => shape.ContentType;

    public override long Size => shape.Length;

    public override DateTimeOffset? LastModified => shape.LastModified;

    public override Stream OpenReadStream(CancellationToken cancellationToken = default) =>
        new MemoryStream(frame, offset, shape.Length, writable: false);

    internal byte[] ToArray() => frame.AsSpan(offset, shape.Length).ToArray();

    // What the header says of a blob, so the reader can find it and the handler sees the file it was sent.
    internal sealed record Shape(string Name, string ContentType, int Length, DateTimeOffset? LastModified)
    {
        internal void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("n", Name);
            writer.WriteString("t", ContentType);
            writer.WriteNumber("l", Length);
            if (LastModified is { } modified)
            {
                writer.WriteNumber("d", modified.ToUnixTimeMilliseconds());
            }

            writer.WriteEndObject();
        }

        internal static List<Shape> ReadAll(ref Utf8JsonReader reader)
        {
            WireJson.ExpectStartArray(ref reader, "f");
            var shapes = new List<Shape>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                shapes.Add(Read(ref reader));
            }

            return shapes;
        }

        private static Shape Read(ref Utf8JsonReader reader)
        {
            WireJson.ExpectStartObject(ref reader, "f");
            string name = "", contentType = "";
            var length = 0;
            DateTimeOffset? modified = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var property = reader.GetString();
                reader.Read();
                switch (property)
                {
                    case "n":
                        name = WireJson.ReadRequiredString(ref reader, "n");
                        break;
                    case "t":
                        contentType = WireJson.ReadRequiredString(ref reader, "t");
                        break;
                    case "l":
                        length = WireJson.ReadInt32(ref reader, "l");
                        break;
                    case "d":
                        modified = DateTimeOffset.FromUnixTimeMilliseconds(WireJson.ReadInt64(ref reader, "d"));
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return new Shape(name, contentType, length, modified);
        }
    }
}
