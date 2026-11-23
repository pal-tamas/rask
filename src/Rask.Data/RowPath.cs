using System.Collections;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     What leads a form from its aggregate to one tracked child row: <c>""</c> for the aggregate itself,
///     <c>Pistols[2].</c> for a child, <c>Pistols[2].Parts[0].</c> for one of its own.
/// </summary>
/// <remarks>
///     The same path a generated model's rules name a row by (<c>Lines[2].</c>): the row's place in the collection that
///     holds it, which is the place the form's list shows it in. Only a child held by an <see cref="IAggregate" /> has
///     one; a plain EF Core graph is no form's, and its rows keep their own names.
/// </remarks>
internal static class RowPath
{
    // The same ceiling AggregateChildren walks down with.
    private const int Deepest = 4;

    /// <summary>
    ///     The path to <paramref name="row" />; <c>""</c> when no tracked aggregate holds it, so its fields keep their own
    ///     names.
    /// </summary>
    internal static string Of(DbContext context, EntityEntry row)
    {
        if (row.Entity is IAggregate)
        {
            return "";
        }

        var tracker = context.ChangeTracker;
        var detecting = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;

        try
        {
            return Walk(tracker, row);
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detecting;
        }
    }

    private static string Walk(ChangeTracker tracker, EntityEntry row)
    {
        var segments = new List<string>();
        var current = row;

        for (var depth = 0; depth < Deepest; depth++)
        {
            if (current.Entity is IAggregate)
            {
                segments.Reverse();
                return string.Concat(segments);
            }

            if (Holder(tracker, current) is not var (holder, segment))
            {
                return "";
            }

            segments.Add(segment);
            current = holder;
        }

        return "";
    }

    // The tracked row whose navigation holds `row`, and the segment that names it there.
    private static (EntityEntry Holder, string Segment)? Holder(ChangeTracker tracker, EntityEntry row)
    {
        foreach (var navigation in row.Metadata.GetForeignKeys().Select(static key => key.PrincipalToDependent).OfType<INavigation>())
        {
            foreach (var candidate in tracker.Entries())
            {
                if (!navigation.DeclaringEntityType.IsAssignableFrom(candidate.Metadata) ||
                    navigation.GetGetter().GetClrValue(candidate.Entity) is not { } held)
                {
                    continue;
                }

                if (!navigation.IsCollection)
                {
                    if (ReferenceEquals(held, row.Entity))
                    {
                        return (candidate, navigation.Name + ".");
                    }

                    continue;
                }

                if (PlaceOf((IEnumerable)held, row.Entity) is { } place)
                {
                    return (candidate, string.Create(CultureInfo.InvariantCulture, $"{navigation.Name}[{place}]."));
                }
            }
        }

        return null;
    }

    private static int? PlaceOf(IEnumerable children, object child)
    {
        var place = 0;
        foreach (var each in children)
        {
            if (ReferenceEquals(each, child))
            {
                return place;
            }

            place++;
        }

        return null;
    }
}
