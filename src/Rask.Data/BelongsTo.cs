using System.ComponentModel;

namespace Rask.Data;

/// <summary>Makes a <see cref="BelongsTo{TAggregate}" /> of an id, for the generated property to hand out.</summary>
/// <remarks>Not a type to call: a <c>BelongsTo</c> is read off the property Rask generates.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BelongsTo
{
    /// <summary>The reference to the <typeparamref name="TAggregate" /> <paramref name="id" /> names.</summary>
    /// <typeparam name="TAggregate">The aggregate referred to.</typeparam>
    /// <typeparam name="TId">Its key type.</typeparam>
    /// <param name="id">The stored id.</param>
    /// <returns>The reference.</returns>
    public static BelongsTo<TAggregate> Of<TAggregate, TId>(TId id)
        where TAggregate : Aggregate<TId>
        where TId : notnull
    {
        ArgumentNullException.ThrowIfNull(id);
        return new BelongsTo<TAggregate>(id);
    }
}
