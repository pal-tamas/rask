namespace Rask.Data;

/// <summary>
///     The key of a tenant or user aggregate keyed by a typed id — <c>TenantId(Guid Value)</c> — read from the
///     <see cref="Guid" /> the framework carries through the value the id holds.
/// </summary>
/// <typeparam name="TKey">The typed id.</typeparam>
/// <typeparam name="TValue">What it holds: a <see cref="Guid" />, an <see cref="int" /> or a <see cref="long" />.</typeparam>
/// <param name="wrap">The id holding a value: <c>value =&gt; new TenantId(value)</c>.</param>
internal sealed class TypedCurrentRowKey<TKey, TValue>(Func<TValue, TKey> wrap) : CurrentRowKey<TKey>
    where TKey : struct
    where TValue : struct
{
    /// <inheritdoc />
    internal override bool IsNumber => CurrentKey.IsNumber<TValue>();

    /// <inheritdoc />
    internal override TKey Of(Guid key, CurrentRowKind kind, string keyedBy) => wrap(CurrentKey.Of<TValue>(key, kind, keyedBy));
}
