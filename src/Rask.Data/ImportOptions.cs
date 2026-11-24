using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>How <c>X.Import</c> loads its rows: <see cref="BatchSize" />, <see cref="OnInvalid" />, <see cref="Upsert{TKey}" />.</summary>
/// <typeparam name="T">The aggregate each row becomes.</typeparam>
/// <typeparam name="TRow">The rows imported.</typeparam>
public sealed class ImportOptions<[DynamicallyAccessedMembers(DataTrimming.Entity)] T, TRow>
    where T : class
{
    internal ImportOptions()
    {
    }

    /// <summary>
    ///     How many rows one transaction saves — 500 unless set; 1 to 10 000. A larger batch is fewer round trips and a
    ///     longer lock; each batch starts from an empty change tracker, so memory stays flat however many rows there are.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>What a refused row does: <see cref="ImportFailure.Stop" /> the import (unless set), or <see cref="ImportFailure.Collect" /> it.</summary>
    public ImportFailure OnInvalid { get; set; } = ImportFailure.Stop;

    /// <summary>The natural key a row is matched by, when an import may meet what an earlier one stored.</summary>
    internal ImportMatch<T, TRow>? Match { get; private set; }

    /// <summary>
    ///     Matches each row's aggregate to the stored one with the same <paramref name="key" />, and changes that one with
    ///     <paramref name="update" /> instead of storing a second — <c>o.Upsert(t =&gt; t.SourceId, (t, row) =&gt; t.Follow(row))</c>.
    ///     Two rows of one import with the same key are one aggregate too: the first makes it, the next changes it.
    /// </summary>
    /// <typeparam name="TKey">The key: a value object, a typed id, a text.</typeparam>
    /// <param name="key">The property that names one aggregate, read from what the factory built and from what is stored.</param>
    /// <param name="update">Changes the stored aggregate through its own method, from the row.</param>
    /// <exception cref="ArgumentNullException"><paramref name="key" /> or <paramref name="update" /> is null.</exception>
    public void Upsert<TKey>(Expression<Func<T, TKey>> key, Action<T, TRow> update)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(update);
        Match = new ImportMatch<T, TRow, TKey>(key, update);
    }
}
