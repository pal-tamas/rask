using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data;

/// <summary>How a battery's table says what its rows become when its key turns from a store-numbered integer into a Guid.</summary>
internal static class StoreKeyRebuildBuilderExtensions
{
    /// <summary>
    ///     Marks the table for a key rebuild: the migration that turns its integer key into a Guid refuses while a row has no
    ///     <paramref name="pending" />, then keeps the rest under ids made from <paramref name="keepBy" /> — or drops them,
    ///     history, when <paramref name="keepBy" /> is null.
    /// </summary>
    /// <param name="builder">The table's entity.</param>
    /// <param name="pending">The property that is null on a row still to be processed, or null when no row ever waits.</param>
    /// <param name="keepBy">The instant a kept row's new id is made from, or null when the rows are dropped.</param>
    internal static EntityTypeBuilder RebuildsStoreKey(this EntityTypeBuilder builder, string? pending, string? keepBy)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.HasAnnotation(StoreKeyRebuild.Annotation, StoreKeyRebuild.Format(pending, keepBy));
    }
}
