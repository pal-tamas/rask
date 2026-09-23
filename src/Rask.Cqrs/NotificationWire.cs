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

    /// <summary>
    ///     The contract a subscription record of <paramref name="type" /> crosses with, or null when it has none. Its
    ///     result codec is what carries each delivered notification, so a contract without one cannot be opened
    ///     remotely — the same "no wire form, so in-process only" answer an uncodeable notification gets.
    /// </summary>
    public static RemoteContract? SubscriptionContractFor(Type type) =>
        RemoteContractRegistry.TryGet(type, out var contract)
        && contract is { Kind: RemoteMessageKind.Subscription, CarriesFiles: false }
        && contract.WriteResult is not null && contract.ReadResult is not null
            ? contract
            : null;

    /// <summary>Writes the subscription record itself, the way a query's message travels.</summary>
    public static byte[] EncodeMessage(RemoteContract contract, object message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            contract.WriteMessage(writer, message, []);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Rebuilds a subscription record from what <see cref="EncodeMessage" /> wrote.</summary>
    public static object DecodeMessage(RemoteContract contract, ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload);
        reader.Read();
        return contract.ReadMessage(ref reader, []);
    }

    /// <summary>
    ///     Writes one delivered notification. A subscription record carries its notification as the contract's result;
    ///     an unscoped subscription is opened on the notification itself, which is its own message.
    /// </summary>
    public static byte[] EncodeEvent(RemoteContract contract, object notification)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            if (contract.Kind == RemoteMessageKind.Subscription)
            {
                contract.WriteResult!(writer, notification);
            }
            else
            {
                contract.WriteMessage(writer, notification, []);
            }
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Rebuilds a delivered notification from what <see cref="EncodeEvent" /> wrote.</summary>
    public static INotification DecodeEvent(RemoteContract contract, ReadOnlySpan<byte> payload)
    {
        var reader = new Utf8JsonReader(payload);
        reader.Read();
        return (INotification)(contract.Kind == RemoteMessageKind.Subscription
            ? contract.ReadResult!(ref reader)!
            : contract.ReadMessage(ref reader, []));
    }
}
