using System.Buffers;
using System.Text.Json;

namespace Rask.Core.Live;

/// <summary>
///     The thread's <see cref="Utf8JsonWriter" />, pointed at one payload for the length of a <c>using</c>.
/// </summary>
/// <remarks>
///     A payload is written synchronously, start to finish, so one writer per thread serves every session
///     that renders on it. A nested rent — nothing does one today — gets a writer of its own.
/// </remarks>
internal readonly struct PayloadJsonWriter : IDisposable
{
    private static readonly ArrayBufferWriter<byte> Nowhere = new(1);

    [ThreadStatic] private static Utf8JsonWriter? _idle;

    private PayloadJsonWriter(Utf8JsonWriter writer) => Writer = writer;

    public Utf8JsonWriter Writer { get; }

    public static PayloadJsonWriter Rent(IBufferWriter<byte> output, JsonWriterOptions options)
    {
        if (_idle is not { } writer)
        {
            return new PayloadJsonWriter(new Utf8JsonWriter(output, options));
        }

        _idle = null;
        writer.Reset(output);
        return new PayloadJsonWriter(writer);
    }

    public void Dispose() => Return(Writer);

    private static void Return(Utf8JsonWriter writer)
    {
        writer.Flush();

        // Let go of the session's buffer: the writer outlives it.
        writer.Reset(Nowhere);
        _idle = writer;
    }
}
