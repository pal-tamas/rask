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
///     Two topics are the same topic when they have the same <see cref="Name" /> and the same message type, so a second
///     <c>new Topic&lt;OrderPlaced&gt;("orders")</c> reaches the first one's subscribers. The type is part of the identity:
///     a <c>Topic&lt;string&gt;("orders")</c> is a different topic, and a message can never arrive as the wrong type.
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

    /// <summary>The topic's name.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({typeof(T).Name})";
}
