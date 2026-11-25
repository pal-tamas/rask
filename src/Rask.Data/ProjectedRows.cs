using System.Collections;

namespace Rask.Data;

/// <summary>
///     The rows of <typeparamref name="T" /> one save added, removed, or changed in a watched member — what a declared
///     projection (<c>b.Projects&lt;E&gt;(…)</c>) makes its event from. Never empty: a save that changes no watched member
///     raises nothing.
/// </summary>
/// <typeparam name="T">The rows' type.</typeparam>
public sealed class ProjectedRows<T> : IReadOnlyList<ProjectedRow<T>>
    where T : class
{
    private readonly List<ProjectedRow<T>> _rows;

    internal ProjectedRows(List<ProjectedRow<T>> rows) => _rows = rows;

    /// <inheritdoc />
    public int Count => _rows.Count;

    /// <inheritdoc />
    public ProjectedRow<T> this[int index] => _rows[index];

    /// <summary>
    ///     Every value <paramref name="member" /> holds across the rows, before and after, each once:
    ///     <c>changed.Values(f =&gt; f.PistolId)</c> — the pistols whose numbering this save moved, the one a row left as
    ///     well as the one it went to.
    /// </summary>
    /// <typeparam name="TValue">The member's type.</typeparam>
    /// <param name="member">What to read from each side of each row.</param>
    /// <returns>The distinct values, in the order first seen.</returns>
    public IReadOnlyList<TValue> Values<TValue>(Func<T, TValue> member)
    {
        ArgumentNullException.ThrowIfNull(member);
        var seen = new HashSet<TValue>();
        var values = new List<TValue>();
        foreach (var row in _rows)
        {
            Take(row.Before);
            Take(row.After);
        }

        return values;

        void Take(T? side)
        {
            if (side is not null && member(side) is var value && seen.Add(value))
            {
                values.Add(value);
            }
        }
    }

    /// <inheritdoc />
    public IEnumerator<ProjectedRow<T>> GetEnumerator() => _rows.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
