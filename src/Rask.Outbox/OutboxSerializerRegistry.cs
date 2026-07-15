using System.Collections.Concurrent;
using System.Text.Json;
using Rask.Cqrs;

namespace Rask.Outbox;

/// <summary>
/// Maps a persisted <see cref="OutboxMessage.Type"/> name back to its CLR type so the
/// <see cref="OutboxProcessor{TContext}"/> can deserialize + publish it. Populated at module load by the
/// <c>Rask.Outbox</c> source generator (one registration per <see cref="IOutboxEvent"/> type it finds), so
/// there is no runtime <c>Type.GetType</c> / assembly scanning.
/// </summary>
public static class OutboxSerializerRegistry
{
    private static readonly ConcurrentDictionary<string, Type> Types = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registers an event type by name. Called by the generated module initializer.</summary>
    public static void RegisterEvent(string typeName, Type type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(type);
        Types[typeName] = type;
    }

    /// <summary>Serializes an outbox event to its stored (type-name, JSON-payload) pair.</summary>
    public static (string Type, string Payload) Serialize(IOutboxEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var type = domainEvent.GetType();
        return (type.FullName ?? type.Name, JsonSerializer.Serialize(domainEvent, type, Json));
    }

    /// <summary>Rehydrates a stored event, or <c>null</c> if its type isn't registered.</summary>
    public static INotification? Deserialize(string typeName, string payload)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(payload);
        return Types.TryGetValue(typeName, out var type)
            ? JsonSerializer.Deserialize(payload, type, Json) as INotification
            : null;
    }
}
