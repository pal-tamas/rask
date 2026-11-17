using System.Buffers;
using System.Text.Json;
using Rask.Wire;

namespace Rask.Cqrs;

// The worker's answer frame, and the page's reading of it. One of:
//
//     {"r": result}                   a result in its generated encoding ({} for a void command)
//     {"b": 1, "f": [shape]}          a byte[] result, carried as the blob rather than as base64
//     {"d": {"n","t"}, "f": [shape]}  a FileDownload, its content the blob
//     {"e": kind, ...}                the handler threw — rethrown on the page as the exception it would have seen
//                                     in-process: invalid (RaskValidationException), forbidden (ForbiddenException),
//                                     canceled (OperationCanceledException), or failed (RemoteDispatchException)
//
// The page and the worker are one app in one browser tab, so an exception's text and stack cross as they are: there is
// no trust boundary between them to hide it behind, and a developer needs both.
internal static class OffThreadAnswer
{
    internal static async Task<byte[]> Result(RemoteContract contract, object? result, CancellationToken cancellationToken)
    {
        if (result is byte[] bytes && contract.ResultType == typeof(byte[]))
        {
            return OffThreadFrame.Compose(Header(w => w.WriteNumber("b", 1), Shape("result", "application/octet-stream", bytes.Length)), bytes);
        }

        if (result is FileDownload download)
        {
            var content = await Content(download, cancellationToken).ConfigureAwait(false);
            return OffThreadFrame.Compose(
                Header(w =>
                {
                    w.WriteStartObject("d");
                    w.WriteString("n", download.FileName);
                    w.WriteString("t", download.ContentType);
                    w.WriteEndObject();
                }, Shape(download.FileName, download.ContentType, content.Length)),
                content);
        }

        return OffThreadFrame.Compose(
            Header(w =>
            {
                if (contract.WriteResult is { } write)
                {
                    w.WritePropertyName("r");
                    write(w, result);
                }
            }, shape: null),
            []);
    }

    internal static byte[] Fault(Exception thrown)
    {
        var header = Header(w =>
        {
            switch (thrown)
            {
                case RaskValidationException invalid:
                    w.WriteString("e", "invalid");
                    WriteFailures(w, invalid.Failures);
                    break;
                case ForbiddenException forbidden:
                    w.WriteString("e", "forbidden");
                    w.WriteBoolean("a", forbidden.IsAuthenticated);
                    break;
                case OperationCanceledException:
                    w.WriteString("e", "canceled");
                    break;
                default:
                    w.WriteString("e", "failed");
                    w.WriteString("t", thrown.GetType().FullName);
                    w.WriteString("x", thrown.ToString());
                    break;
            }

            w.WriteString("m", thrown.Message);
        }, shape: null);

        return OffThreadFrame.Compose(header, []);
    }

    // The page's reading. A result is decoded by the contract's own reader; a fault is thrown.
    internal static TResult Read<TResult>(RemoteContract contract, byte[] frame)
    {
        var (header, blobsStart) = OffThreadFrame.Split(frame);
        var reader = new Utf8JsonReader(header.Span);
        reader.Read();
        WireJson.ExpectStartObject(ref reader, "answer");

        var read = new Received();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();
            read.Take(property, ref reader, contract, frame, blobsStart);
        }

        return read.Kind is { } fault ? throw Rethrown(contract, fault, read) : (TResult)read.Value()!;
    }

    // What an answer header said, property by property.
    private sealed class Received
    {
        private object? _result;
        private bool _bytes;
        private string? _fileName;
        private string? _contentType;
        private List<OffThreadBlob> _blobs = [];

        public string? Kind { get; private set; }

        public string Message { get; private set; } = "";

        public string? Type { get; private set; }

        public string? Detail { get; private set; }

        public bool Authenticated { get; private set; }

        public IReadOnlyList<FieldFailure> Failures { get; private set; } = [];

        public void Take(string? property, ref Utf8JsonReader reader, RemoteContract contract, byte[] frame, int blobsStart)
        {
            switch (property)
            {
                case "r" when contract.ReadResult is { } read:
                    _result = read(ref reader);
                    break;
                case "b":
                    _bytes = true;
                    break;
                case "d":
                    (_fileName, _contentType) = ReadDownload(ref reader);
                    break;
                case "f":
                    _blobs = OffThreadFrame.Blobs(frame, blobsStart, OffThreadBlob.Shape.ReadAll(ref reader));
                    break;
                case "e":
                    Kind = reader.GetString();
                    break;
                case "m":
                    Message = reader.GetString() ?? "";
                    break;
                case "t":
                    Type = reader.GetString();
                    break;
                case "a":
                    Authenticated = reader.GetBoolean();
                    break;
                case "x":
                    Detail = reader.GetString();
                    break;
                case "v":
                    Failures = ReadFailures(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        public object? Value() => (_bytes, _fileName, _blobs.Count) switch
        {
            (true, _, 1) => _blobs[0].ToArray(),
            (_, { } name, 1) => FileDownload.FromBytes(name, _contentType, _blobs[0].ToArray()),
            _ => _result,
        };

        private static List<FieldFailure> ReadFailures(ref Utf8JsonReader reader)
        {
            WireJson.ExpectStartArray(ref reader, "v");
            var failures = new List<FieldFailure>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string message = "";
                string? source = null;
                List<string> fields = [];
                List<string>? marked = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var property = reader.GetString();
                    reader.Read();
                    switch (property)
                    {
                        case "m":
                            message = reader.GetString() ?? "";
                            break;
                        case "fs":
                            fields = ReadStrings(ref reader);
                            break;
                        case "k":
                            marked = ReadStrings(ref reader);
                            break;
                        case "s":
                            source = reader.GetString();
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }

                failures.Add(new FieldFailure(message, fields, marked, source));
            }

            return failures;
        }

        private static List<string> ReadStrings(ref Utf8JsonReader reader)
        {
            var values = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                values.Add(reader.GetString() ?? "");
            }

            return values;
        }

        private static (string Name, string ContentType) ReadDownload(ref Utf8JsonReader reader)
        {
            string name = "download", contentType = "application/octet-stream";
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var property = reader.GetString();
                reader.Read();
                if (property is "n")
                {
                    name = reader.GetString() ?? name;
                }
                else if (property is "t")
                {
                    contentType = reader.GetString() ?? contentType;
                }
            }

            return (name, contentType);
        }
    }

    private static Exception Rethrown(RemoteContract contract, string fault, Received read) => fault switch
    {
        "invalid" => new RaskValidationException(read.Failures),
        "forbidden" => new ForbiddenException(read.Message, read.Authenticated),
        "canceled" => new OperationCanceledException(read.Message),
        _ => new RemoteDispatchException($"'{contract.Name}' failed in its worker: {read.Message}")
        {
            MessageName = contract.Name,
            ProblemType = read.Type,
            Detail = read.Detail,
        },
    };

    private static async Task<byte[]> Content(FileDownload download, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream(download.Length is { } known and <= int.MaxValue ? (int)known : 0);
        await download.WriteTo(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
    }

    private static OffThreadBlob.Shape Shape(string name, string contentType, int length) =>
        new(name, contentType, length, LastModified: null);

    private static byte[] Header(Action<Utf8JsonWriter> write, OffThreadBlob.Shape? shape)
    {
        var header = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(header))
        {
            writer.WriteStartObject();
            write(writer);
            if (shape is not null)
            {
                writer.WriteStartArray("f");
                shape.Write(writer);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return header.WrittenSpan.ToArray();
    }

    private static void WriteFailures(Utf8JsonWriter writer, IReadOnlyList<FieldFailure> failures)
    {
        writer.WriteStartArray("v");
        foreach (var failure in failures)
        {
            writer.WriteStartObject();
            writer.WriteString("m", failure.Message);
            WriteStrings(writer, "fs", failure.Fields);
            if (failure.Marked is { } marked)
            {
                WriteStrings(writer, "k", marked);
            }

            if (failure.Source is { } source)
            {
                writer.WriteString("s", source);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

}
