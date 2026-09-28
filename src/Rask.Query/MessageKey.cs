using System.Diagnostics.CodeAnalysis;

namespace Rask.Querying;

/// <summary>Builds the key a message identifies itself by.</summary>
internal static class MessageKey
{
    /// <summary>
    ///     <c>[typeof(GetOrders), message]</c>.
    /// </summary>
    /// <remarks>
    ///     The type first, so <c>Invalidate&lt;GetOrders&gt;()</c> is a prefix match over every page rather
    ///     than a special case. A <see cref="Type" /> rather than its name: it survives a rename, it cannot
    ///     collide across namespaces, and it can never be mistaken for a hand-written string part — so
    ///     derived and hand-written keys share one cache safely.
    /// </remarks>
    public static QueryKey For(object message) => QueryKey.Of(message.GetType(), message);

    /// <summary>The prefix that matches every entry for a message type.</summary>
    public static QueryKey ForType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type type) =>
        QueryKey.Of(type);
}
