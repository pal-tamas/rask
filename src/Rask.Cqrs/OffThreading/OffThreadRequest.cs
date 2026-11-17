using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Rask.Wire;

namespace Rask.Cqrs;

// The page's half of a request frame: the message in its generated encoding, the files and byte arrays that encoding
// asked to carry beside it, and what the worker needs to run it as the page would — the culture the page reads in.
//
// Header, in this order (the reader needs the blobs before the message that points at them):
//     {"n": wire name, "a": the message's assembly, "c": culture, "u": UI culture, "f": [blob shapes], "m": message}
internal static class OffThreadRequest
{
    internal static async Task<byte[]> Write(RemoteContract contract, object message, CancellationToken cancellationToken)
    {
        // A Collection, not a List: that is what asks the codec to move a byte[] beside the JSON (WireCodecEmitter).
        var files = new Collection<RemoteFile>();
        var body = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(body))
        {
            contract.WriteMessage(writer, message, files);
        }

        var carried = await Measured(files, cancellationToken).ConfigureAwait(false);
        var header = Header(contract, body.WrittenSpan, carried);
        var frame = OffThreadFrame.Allocate(header, carried.Sum(static c => (long)c.Shape.Length), out var offset);
        foreach (var (file, shape, buffered) in carried)
        {
            var slot = frame.AsMemory(offset, shape.Length);
            if (buffered is not null)
            {
                buffered.CopyTo(slot);
            }
            else
            {
                var stream = file.OpenReadStream(cancellationToken);
                await using (stream.ConfigureAwait(false))
                {
                    await stream.ReadExactlyAsync(slot, cancellationToken).ConfigureAwait(false);
                }
            }

            offset += shape.Length;
        }

        return frame;
    }

    // A file whose size is known is read straight into the frame; one whose size is not is read first, to learn it.
    private static async Task<List<(RemoteFile File, OffThreadBlob.Shape Shape, byte[]? Buffered)>> Measured(
        Collection<RemoteFile> files, CancellationToken cancellationToken)
    {
        var carried = new List<(RemoteFile, OffThreadBlob.Shape, byte[]?)>(files.Count);
        foreach (var file in files)
        {
            byte[]? buffered = null;
            var length = file.Size;
            if (length < 0 || length > Array.MaxLength)
            {
                buffered = await ReadAll(file, cancellationToken).ConfigureAwait(false);
                length = buffered.Length;
            }

            carried.Add((file, new OffThreadBlob.Shape(file.Name, file.ContentType, (int)length, file.LastModified), buffered));
        }

        return carried;
    }

    private static async Task<byte[]> ReadAll(RemoteFile file, CancellationToken cancellationToken)
    {
        var stream = file.OpenReadStream(cancellationToken);
        await using (stream.ConfigureAwait(false))
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
            return copy.ToArray();
        }
    }

    private static byte[] Header(
        RemoteContract contract,
        ReadOnlySpan<byte> message,
        List<(RemoteFile File, OffThreadBlob.Shape Shape, byte[]? Buffered)> carried)
    {
        var header = new ArrayBufferWriter<byte>(message.Length + 256);
        using (var writer = new Utf8JsonWriter(header))
        {
            writer.WriteStartObject();
            writer.WriteString("n", contract.Name);
            writer.WriteString("a", contract.MessageType.Assembly.GetName().Name);
            writer.WriteString("c", CultureInfo.CurrentCulture.Name);
            writer.WriteString("u", CultureInfo.CurrentUICulture.Name);
            writer.WriteStartArray("f");
            foreach (var (_, shape, _) in carried)
            {
                shape.Write(writer);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("m");

            // The codec's own output, already JSON: validating it again would read the message a second time.
            writer.WriteRawValue(message, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return header.WrittenSpan.ToArray();
    }

    // The worker's half: everything but the message, which needs the contract the name finds — and finding that may
    // first mean loading the assembly that registers it.
    internal sealed class Received
    {
        private readonly ReadOnlyMemory<byte> _header;

        private Received(ReadOnlyMemory<byte> header) => _header = header;

        public string Name { get; private set; } = "";

        public string Assembly { get; private set; } = "";

        public string Culture { get; private set; } = "";

        public string UiCulture { get; private set; } = "";

        public IReadOnlyList<RemoteFile> Files { get; private set; } = [];

        internal static Received Parse(byte[] frame)
        {
            var (header, blobsStart) = OffThreadFrame.Split(frame);
            var received = new Received(header);
            var reader = new Utf8JsonReader(header.Span);
            reader.Read();
            WireJson.ExpectStartObject(ref reader, "request");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var property = reader.GetString();
                reader.Read();
                switch (property)
                {
                    case "n":
                        received.Name = WireJson.ReadRequiredString(ref reader, "n");
                        break;
                    case "a":
                        received.Assembly = WireJson.ReadRequiredString(ref reader, "a");
                        break;
                    case "c":
                        received.Culture = WireJson.ReadRequiredString(ref reader, "c");
                        break;
                    case "u":
                        received.UiCulture = WireJson.ReadRequiredString(ref reader, "u");
                        break;
                    case "f":
                        received.Files = OffThreadFrame.Blobs(frame, blobsStart, OffThreadBlob.Shape.ReadAll(ref reader));
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return received;
        }

        internal object Message(RemoteContract contract)
        {
            var reader = new Utf8JsonReader(_header.Span);
            reader.Read();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var property = reader.GetString();
                reader.Read();
                if (property is "m")
                {
                    return contract.ReadMessage(ref reader, Files);
                }

                reader.Skip();
            }

            throw new InvalidDataException($"The off-thread request for '{Name}' carried no message.");
        }
    }
}
