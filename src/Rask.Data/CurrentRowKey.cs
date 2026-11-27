namespace Rask.Data;

/// <summary>
///     How a <see cref="CurrentRow{TRead,TKey}" /> reads the <see cref="Guid" /> the framework carries as the
///     <typeparamref name="TKey" /> its aggregate is keyed by.
/// </summary>
/// <remarks>
///     The plain key — a <see cref="Guid" />, a <see cref="string" />, an <see cref="int" /> or a <see cref="long" /> —
///     is <see cref="CurrentKey" />'s; a typed id over one of them is <see cref="TypedCurrentRowKey{TKey,TValue}" />.
/// </remarks>
/// <typeparam name="TKey">The aggregate's key.</typeparam>
internal class CurrentRowKey<TKey>
{
    /// <summary>The key read as itself.</summary>
    internal static readonly CurrentRowKey<TKey> Plain = new();

    /// <summary>Whether the key is a number, carried inside the <see cref="Guid" /> (<see cref="TenantNumber" />).</summary>
    internal virtual bool IsNumber => CurrentKey.IsNumber<TKey>();

    /// <summary><paramref name="key" /> as the <typeparamref name="TKey" /> <paramref name="keyedBy" /> is keyed by.</summary>
    /// <param name="key">The key as the framework carries it.</param>
    /// <param name="kind">Whose key it is, for the sentence a key that is not one throws.</param>
    /// <param name="keyedBy">The aggregate keyed by <typeparamref name="TKey" />, as the sentence names it.</param>
    internal virtual TKey Of(Guid key, CurrentRowKind kind, string keyedBy) => CurrentKey.Of<TKey>(key, kind, keyedBy);
}
