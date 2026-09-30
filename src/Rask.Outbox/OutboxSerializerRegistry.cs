using System.Collections.Concurrent;
using System.Text.Json;
using Rask.Cqrs;

namespace Rask.Outbox;

/// <summary>
/// Maps a persisted <see cref="OutboxMessage.Type"/> name back to its CLR type so the
/// <see cref="OutboxProcessor{TContext}"/> can deserialize + run it. Every event with an
/// <see cref="IDurableHandler{TEvent}"/> is known already — the Rask.Cqrs source generator records it beside the
/// handler — so there is no runtime <c>Type.GetType</c> / assembly scanning. <see cref="RegisterEvent"/> adds a name
/// by hand: the old name of a renamed event, whose stored rows still carry it.
/// </summary>
public static class OutboxSerializerRegistry
{
    private static readonly ConcurrentDictionary<string, Type> _manual = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Registers an event type by name — the old name of a renamed event, so its stored rows still run.</summary>
    /// <param name="typeName">The name stored rows carry.</param>
    /// <param name="type">The event type to read them as.</param>
    public static void RegisterEvent(string typeName, Type type)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(type);
        _manual[typeName] = type;
    }

    /// <summary>Serializes an event for storage, returning its stored type name and JSON payload.</summary>
    /// <param name="e">The event.</param>
    public static (string Type, string Payload) Serialize(IEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var type = e.GetType();
        return (CqrsRegistry.NameOf(type), JsonSerializer.Serialize(e, type, Json));
    }

    /// <summary>Reads a stored event back, or <c>null</c> when no type is known by <paramref name="typeName"/>.</summary>
    /// <param name="typeName">The stored type name.</param>
    /// <param name="payload">The stored JSON.</param>
    public static IEvent? Deserialize(string typeName, string payload)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(payload);
        var type = _manual.TryGetValue(typeName, out var manual) ? manual : CqrsRegistry.FindDurableEvent(typeName);
        return type is null ? null : JsonSerializer.Deserialize(payload, type, Json) as IEvent;
    }
}
