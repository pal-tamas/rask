namespace Rask.Querying;

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
