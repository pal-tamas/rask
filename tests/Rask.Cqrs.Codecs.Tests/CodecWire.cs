using System.Text;
using System.Text.Json;

namespace Rask.Cqrs.Codecs.Tests;

/// <summary>Drives the generated codecs the way a transport does: one message, or one result, to JSON and back.</summary>
internal static class CodecWire
{
    public static RemoteContract Contract<TMessage>()
    {
        Assert.True(RemoteContractRegistry.TryGet(typeof(TMessage), out var contract),
            $"No contract was generated for {typeof(TMessage)}.");
        return contract!;
    }

    public static string Write<TMessage>(TMessage message, List<RemoteFile>? files = null)
        where TMessage : notnull
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Contract<TMessage>().WriteMessage(writer, message, files ?? []);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static object Read(RemoteContract contract, string json, IReadOnlyList<RemoteFile> files)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return contract.ReadMessage(ref reader, files);
    }

    public static TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : notnull
    {
        var files = new List<RemoteFile>();
        var json = Write(message, files);
        return (TMessage)Read(Contract<TMessage>(), json, files);
    }

    public static string WriteResult<TMessage>(object? result)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Contract<TMessage>().WriteResult!(writer, result);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static object? ReadResult<TMessage>(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return Contract<TMessage>().ReadResult!(ref reader);
    }
}
