namespace Rask.Query;

/// <summary>
///     Which cache entries a <c>QueryKey</c> reaches: everything beneath it, or only itself.
/// </summary>
/// <remarks>
///     <code>
///     QueryClient.Invalidate(QueryKey.Of("orders"));             // lists and details alike
///     QueryClient.Invalidate(QueryKey.Of("orders", id).Only());  // that one entry
///     QueryClient.Invalidate("orders");                          // a name is a one-part key
///     </code>
///     <para>
///         A key converts to this on its own, so the prefix form — the one you want nearly always — needs
///         nothing said. <see cref="QueryKey.Only" /> is the other one.
///     </para>
/// </remarks>
/// <param name="Key">The key to match.</param>
/// <param name="Exact">
///     Whether only <paramref name="Key" /> itself matches, rather than every key that starts with it.
/// </param>
public readonly record struct QueryMatch(QueryKey Key, bool Exact)
{
    /// <summary>Every entry whose key starts with <paramref name="key" /> — the usual meaning.</summary>
    /// <param name="key">The prefix to match.</param>
    public static implicit operator QueryMatch(QueryKey key) => new(key, Exact: false);

    /// <summary>A name is a one-part key, so <c>Invalidate("orders")</c> needs nothing spelled out.</summary>
    /// <param name="name">The key's single part.</param>
    public static implicit operator QueryMatch(string name) => new(QueryKey.Of(name), Exact: false);

    /// <summary>A type is a one-part key, matching every <c>QueryKey.For&lt;T&gt;(…)</c> beneath it.</summary>
    /// <param name="type">The type the data is about.</param>
    public static implicit operator QueryMatch(Type type) => new(QueryKey.Of(type), Exact: false);

    /// <summary>Whether <paramref name="candidate" /> is one of the entries this reaches.</summary>
    /// <param name="candidate">A cached entry's key.</param>
    public bool Reaches(QueryKey candidate) => Exact ? candidate == Key : candidate.Matches(Key);
}
