using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     What a table whose key moves from a store-numbered integer to a client-made <see cref="Guid" /> does with the rows it
///     holds: which column says a row is still pending (and so must not be there), and which instant the kept rows' new
///     ids are made from — or none, and the rows left are history the rebuild drops.
/// </summary>
/// <remarks>
///     Carried as an entity-type annotation (<c>RebuildsStoreKey</c>), so the battery that owns the table says it and the
///     differ, which knows nothing of batteries, reads it off the target model. A table without it is never rebuilt.
/// </remarks>
/// <param name="Pending">The property that is null on a row still to be processed, or null when no row ever waits.</param>
/// <param name="KeepBy">The instant a kept row's new id is made from, or null when the rows are dropped.</param>
internal sealed record StoreKeyRebuild(string? Pending, string? KeepBy)
{
    /// <summary>The entity-type annotation: <c>"{pending}|{keepBy}"</c>, either side empty.</summary>
    internal const string Annotation = "Rask:StoreKeyRebuild";

    /// <summary>The annotation's value for <paramref name="pending" /> and <paramref name="keepBy" />.</summary>
    internal static string Format(string? pending, string? keepBy) => $"{pending}|{keepBy}";

    /// <summary>What <paramref name="table" />'s entity says of its rows, or null when it says nothing.</summary>
    internal static StoreKeyRebuild? Of(ITable table)
    {
        foreach (var mapping in table.EntityTypeMappings)
        {
            if (mapping.TypeBase.FindAnnotation(Annotation)?.Value is not string value)
            {
                continue;
            }

            var bar = value.IndexOf('|', StringComparison.Ordinal);
            var pending = bar < 0 ? value : value[..bar];
            var keepBy = bar < 0 ? "" : value[(bar + 1)..];
            return new StoreKeyRebuild(
                pending.Length == 0 ? null : pending,
                keepBy.Length == 0 ? null : keepBy);
        }

        return null;
    }
}
