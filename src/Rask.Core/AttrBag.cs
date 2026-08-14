using System.Collections;

namespace Rask.Core;

/// <summary>
///     The backing store for a <c>data-*</c> / <c>aria-*</c> bag written as
///     <c>.Data("test-id", "primary")</c> rather than as a dictionary literal.
/// </summary>
/// <remarks>
///     <para>
///         It exists for the shape that dominates real markup: ONE attribute. A
///         <see cref="Dictionary{TKey,TValue}" /> costs three allocations for that — the dictionary, its
///         bucket array and its entry array — on every render of every element carrying one. This costs
///         one object with two fields and no hashing at all, and <c>Element</c> writes it through a
///         direct branch, so the render path never materialises an enumerator either.
///     </para>
///     <para>
///         Lookup is a linear scan. That is the right structure at this size: the bag is written once and
///         read once per render, and for a handful of entries a scan beats hashing them. It is not a
///         general-purpose dictionary and is not meant to grow into one — pass a real
///         <see cref="Dictionary{TKey,TValue}" /> when a bag is genuinely large.
///     </para>
/// </remarks>
public sealed class AttrBag : IReadOnlyDictionary<string, string?>
{
    // The first pair is inlined so the overwhelmingly common single-attribute bag allocates nothing
    // beyond this object; _rest stays null until there is a second one.
    private readonly string _name0;
    private readonly string? _value0;
    private readonly KeyValuePair<string, string?>[]? _rest;

    /// <summary>One attribute — the shape <c>.Data("test-id", "primary")</c> produces.</summary>
    public AttrBag(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _name0 = name;
        _value0 = value;
    }

    /// <summary>Several attributes, in the order given. A later duplicate name wins, as a dictionary literal would.</summary>
    public AttrBag(ReadOnlySpan<(string Name, string? Value)> pairs)
    {
        if (pairs.IsEmpty)
        {
            throw new ArgumentException("An attribute bag needs at least one entry.", nameof(pairs));
        }

        ArgumentException.ThrowIfNullOrEmpty(pairs[0].Name, nameof(pairs));
        _name0 = pairs[0].Name;
        _value0 = pairs[0].Value;
        if (pairs.Length == 1)
        {
            return;
        }

        var rest = new KeyValuePair<string, string?>[pairs.Length - 1];
        for (var i = 1; i < pairs.Length; i++)
        {
            ArgumentException.ThrowIfNullOrEmpty(pairs[i].Name, nameof(pairs));
            rest[i - 1] = new KeyValuePair<string, string?>(pairs[i].Name, pairs[i].Value);
        }

        _rest = rest;
    }

    public int Count => _rest is null ? 1 : _rest.Length + 1;

    public IEnumerable<string> Keys
    {
        get
        {
            yield return _name0;
            if (_rest is null)
            {
                yield break;
            }

            foreach (var kv in _rest)
            {
                yield return kv.Key;
            }
        }
    }

    public IEnumerable<string?> Values
    {
        get
        {
            yield return _value0;
            if (_rest is null)
            {
                yield break;
            }

            foreach (var kv in _rest)
            {
                yield return kv.Value;
            }
        }
    }

    public string? this[string key] =>
        TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

    public bool ContainsKey(string key) => TryGetValue(key, out _);

    public bool TryGetValue(string key, out string? value)
    {
        // Reverse order, so a later duplicate wins the way re-assigning a dictionary key would.
        if (_rest is not null)
        {
            for (var i = _rest.Length - 1; i >= 0; i--)
            {
                if (string.Equals(_rest[i].Key, key, StringComparison.Ordinal))
                {
                    value = _rest[i].Value;
                    return true;
                }
            }
        }

        if (string.Equals(_name0, key, StringComparison.Ordinal))
        {
            value = _value0;
            return true;
        }

        value = null;
        return false;
    }

    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
    {
        yield return new KeyValuePair<string, string?>(_name0, _value0);
        if (_rest is null)
        {
            yield break;
        }

        foreach (var kv in _rest)
        {
            yield return kv;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Read by Element's attribute writer, which branches on this type before its Dictionary<,> fast path
    // and writes the pairs directly. Without that branch a bag would fall to the interface `foreach`,
    // which boxes an enumerator on every render of every element carrying one — losing most of what the
    // type is for.
    internal string Name0 => _name0;

    internal string? Value0 => _value0;

    internal KeyValuePair<string, string?>[]? Rest => _rest;
}
