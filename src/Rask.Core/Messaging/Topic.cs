using System.Text.Json.Serialization.Metadata;

namespace Rask.Core.Messaging;

/// <summary>
///     A named channel that carries messages of one type through <see cref="IBroadcast" />. Declare each topic once, as a
///     <c>static readonly</c> field, and publish and subscribe through that field:
///     <code>
///     public static class Topics
///     {
///         public static readonly Topic&lt;OrderPlaced&gt; Orders = new("orders");
///     }
///     </code>
/// </summary>
/// <remarks>
///     <para>
///         Two topics are the same topic when they have the same <see cref="Name" /> and the same message type, so a
///         second <c>new Topic&lt;OrderPlaced&gt;("orders")</c> reaches the first one's subscribers. The type is part of
///         the identity: a <c>Topic&lt;string&gt;("orders")</c> is a different topic, and a message can never arrive as
///         the wrong type.
///     </para>
///     <para>
///         A topic stays inside this process unless it is declared with a <see cref="JsonTypeInfo{T}" />:
///         <c>new Topic&lt;OrderPlaced&gt;("orders", AppJson.Default.OrderPlaced)</c>. With a backplane registered
///         (<c>AddRaskRedisBackplane()</c> from <c>Rask.Redis</c>), such a topic's messages are serialized through it and
///         reach the subscribers on every host. Without one it behaves exactly like a local topic.
///     </para>
/// </remarks>
/// <typeparam name="T">The message type the topic carries.</typeparam>
public sealed class Topic<T>
{
    /// <summary>Creates a topic named <paramref name="name" />.</summary>
    /// <param name="name">The topic's name; not empty.</param>
    public Topic(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>
    ///     Creates a topic named <paramref name="name" /> whose messages cross to the other hosts of the app when a
    ///     backplane is registered, serialized with <paramref name="crossHost" />.
    /// </summary>
    /// <param name="name">The topic's name; not empty. Every host must declare it with the same message type.</param>
    /// <param name="crossHost">
    ///     The source-generated JSON contract for <typeparamref name="T" />, such as
    ///     <c>AppJson.Default.OrderPlaced</c> from a <c>JsonSerializerContext</c>.
    /// </param>
    public Topic(string name, JsonTypeInfo<T> crossHost)
        : this(name)
    {
        ArgumentNullException.ThrowIfNull(crossHost);
        CrossHost = crossHost;
    }

    /// <summary>The topic's name.</summary>
    public string Name { get; }

    /// <summary>The contract a cross-host message is serialized with; <see langword="null" /> for a local topic.</summary>
    internal JsonTypeInfo<T>? CrossHost { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({typeof(T).Name})";
}
