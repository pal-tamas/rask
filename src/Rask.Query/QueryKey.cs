using System.Diagnostics.CodeAnalysis;

namespace Rask.Query;

/// <summary>
///     What identifies one cache entry: an ordered list of parts, matched by prefix.
/// </summary>
/// <remarks>
///     <para>
///         The same shape TanStack Query uses, for the same reason. A flat key can only be compared for
///         equality, so invalidation is all-or-one; an ordered key can be matched by its <em>start</em>,
///         which is what lets <c>["orders"]</c> refresh every list and every detail beneath it.
///     </para>
///     <para>
///         You rarely write one. For a CQRS message the message <em>is</em> the key — Rask messages are
///         records, so structural equality comes free, and <c>new GetOrders(Page: 1)</c> written in two
///         components is one entry and one round trip with nothing to keep in sync. Write a key when you
///         want a hierarchy across message types, or for data that does not arrive through CQRS at all.
///     </para>
///     <para>
///         Order matters across parts and does not matter inside a <see cref="QueryKeyFields" />, which is
///         exactly TanStack's rule: <c>["orders", "list"]</c> is not <c>["list", "orders"]</c>, but
///         <c>{ page, status }</c> and <c>{ status, page }</c> are the same key.
///     </para>
/// </remarks>
public readonly struct QueryKey : IEquatable<QueryKey>
{
    private readonly object?[] _parts;

    private QueryKey(object?[] parts) => _parts = parts;

    /// <summary>Builds a key from its parts, in order.</summary>
    /// <param name="parts">
    ///     Compared with <see cref="object.Equals(object)" />, so records, strings, numbers and
    ///     <see cref="Type" /> all work. A mutable object makes a bad part, for the same reason it makes a
    ///     bad dictionary key.
    /// </param>
    /// <exception cref="ArgumentException">There are no parts. An empty key would match everything.</exception>
    public static QueryKey Of(params object?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
        {
            // Silently matching everything is how one careless call clears a whole cache. If that is
            // what you meant, IQueryClient.InvalidateAll says so out loud.
            throw new ArgumentException("A query key needs at least one part.", nameof(parts));
        }

        return new QueryKey((object?[])parts.Clone());
    }

    /// <summary>A named-value part, whose fields are matched as a <em>subset</em>.</summary>
    /// <remarks>
    ///     The C# stand-in for TanStack's object part. Sorted on construction, so field order does not
    ///     affect equality — and built from named pairs rather than reflected off an anonymous type,
    ///     because reflection here would warn under the trimmer on a WASM publish.
    /// </remarks>
    /// <summary>
    ///     A single-part key from a plain name, so a caller with one string does not have to reach for
    ///     <see cref="Of" />. Implicit because the two spellings mean exactly the same thing.
    /// </summary>
    public static implicit operator QueryKey(string name) => Of(name);

    public static QueryKeyFields Fields(params (string Name, object? Value)[] fields) => new(fields);

    /// <summary>The parts, in order.</summary>
    public IReadOnlyList<object?> Parts => _parts ?? [];

    /// <summary>How many parts this key has.</summary>
    public int Count => _parts?.Length ?? 0;

    /// <summary>
    ///     Whether this key is matched by <paramref name="filter" />.
    /// </summary>
    /// <remarks>
    ///     Mirrors TanStack's <c>partialMatchKey</c>: a filter longer than the key never matches, each
    ///     position is compared by equality, and a <see cref="QueryKeyFields" /> filter part is a subset
    ///     test rather than an equality one.
    /// </remarks>
    public bool Matches(QueryKey filter)
    {
        if (filter.Count > Count)
        {
            return false;
        }

        for (var i = 0; i < filter.Count; i++)
        {
            var mine = _parts![i];
            var theirs = filter._parts![i];

            if (theirs is QueryKeyFields wanted)
            {
                if (mine is not QueryKeyFields have || !have.Contains(wanted))
                {
                    return false;
                }

                continue;
            }

            if (!Equals(mine, theirs))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(QueryKey other)
    {
        if (Count != other.Count)
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            if (!Equals(_parts![i], other._parts![i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is QueryKey other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Accumulated rather than materialised: this runs on every cache read, and a LINQ chain here
        // would allocate per lookup.
        var hash = default(HashCode);
        for (var i = 0; i < Count; i++)
        {
            hash.Add(_parts![i]);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(QueryKey left, QueryKey right) => left.Equals(right);

    public static bool operator !=(QueryKey left, QueryKey right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => "[" + string.Join(", ", Parts.Select(Describe)) + "]";

    private static string Describe(object? part) => part switch
    {
        null => "null",
        Type type => type.Name,
        string text => "'" + text + "'",
        _ => part.ToString() ?? string.Empty,
    };
}

/// <summary>
///     A set of named values inside a query key, matched as a subset.
/// </summary>
/// <remarks>
///     <c>Fields(("status", "done"))</c> matches a key holding
///     <c>Fields(("page", 1), ("status", "done"))</c>, which is what makes "every done order, any page"
///     expressible. Sorted by name on construction, so the order they were written in does not change
///     the key.
/// </remarks>
public sealed class QueryKeyFields : IEquatable<QueryKeyFields>
{
    private readonly (string Name, object? Value)[] _fields;

    internal QueryKeyFields((string Name, object? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        _fields = [.. fields.OrderBy(f => f.Name, StringComparer.Ordinal)];
    }

    /// <summary>The fields, ordered by name.</summary>
    public IReadOnlyList<(string Name, object? Value)> Fields => _fields;

    /// <summary>Whether every field in <paramref name="other" /> is present here with the same value.</summary>
    public bool Contains(QueryKeyFields other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (var (name, value) in other._fields)
        {
            var found = false;
            foreach (var (mine, held) in _fields)
            {
                if (string.Equals(mine, name, StringComparison.Ordinal))
                {
                    if (!Equals(held, value))
                    {
                        return false;
                    }

                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(QueryKeyFields? other)
    {
        if (other is null || other._fields.Length != _fields.Length)
        {
            return false;
        }

        for (var i = 0; i < _fields.Length; i++)
        {
            if (!string.Equals(_fields[i].Name, other._fields[i].Name, StringComparison.Ordinal)
                || !Equals(_fields[i].Value, other._fields[i].Value))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as QueryKeyFields);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var (name, value) in _fields)
        {
            hash.Add(name, StringComparer.Ordinal);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() =>
        "{ " + string.Join(", ", _fields.Select(f => f.Name + ": " + (f.Value?.ToString() ?? "null"))) + " }";
}

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
