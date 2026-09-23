using System.Buffers;
using System.Text.Json;

namespace Rask.Cqrs;

/// <summary>
///     A notification as bytes, through its generated wire codec — the one form that crosses to another host and to a
///     browser, so both read exactly what the other wrote.
/// </summary>
internal static class NotificationWire
{
    /// <summary>
    ///     The contract a notification of <paramref name="type" /> crosses with, or null when it has none: no codec was
    ///     generated for it (the app references no transport), or it carries files, which live only in their request.
    /// </summary>
    public static RemoteContract? ContractFor(Type type) =>
        RemoteContractRegistry.TryGet(type, out var contract)
        && contract is { Kind: RemoteMessageKind.Notification, CarriesFiles: false }
            ? contract
            : null;

    public static byte[] Encode(RemoteContract contract, object notification)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            contract.WriteMessage(writer, notification, []);
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static INotification Decode(RemoteContract contract, ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload);
        reader.Read();
        return (INotification)contract.ReadMessage(ref reader, []);
    }
}
