using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     The declared unique rules (<c>IsUnique("…")</c>) the rows of one save would break — between two rows of the save,
///     and against a stored row — asked as queries (<see cref="DeclaredUniqueRules" />), one per rule a row's change
///     touches.
/// </summary>
/// <remarks>
///     What the save over a schema Rask does not own asks before it writes (<see cref="DeclaredRuleCheckInterceptor" />),
///     and what a dry run asks of a save it never makes (<see cref="DryRun" />), so a form says "already taken" as the
///     field is left. A rule is touched by a new row, and by a changed row whose change is to a column the rule names —
///     or to any column, for a rule filtered with <c>Where(…)</c>, whose rows a change can move in or out of.
/// </remarks>
internal static class UniqueRulesOfSave
{
    /// <summary>Each row of the save, with the declared unique rules its change touches; a row that touches none is left out.</summary>
    internal static List<(EntityEntry Row, List<IIndex> Rules)> Touched(DbContext context)
    {
        var touched = new List<(EntityEntry, List<IIndex>)>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var rules = DeclaredUniqueRules.Rules(entry.Metadata).Where(rule => Touches(entry, rule)).ToList();
            if (rules.Count > 0)
            {
                touched.Add((entry, rules));
            }
        }

        return touched;
    }

    /// <summary>The failures the touched rows cause: one per rule two of them, or one and a stored row, both satisfy.</summary>
    /// <param name="context">The context to ask through — its filters decide which rows count.</param>
    /// <param name="rows">What <see cref="Touched" /> found.</param>
    /// <param name="valueOf">A row's value for one of its properties, by name, as it will be written.</param>
    /// <param name="placeOf">What leads to a row on its aggregate's form — <c>""</c> for the root, <c>Parts[1].</c> for a child.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    internal static async Task<List<FieldFailure>> FailuresAsync(
        DbContext context,
        List<(EntityEntry Row, List<IIndex> Rules)> rows,
        Func<EntityEntry, string, object?> valueOf,
        Func<EntityEntry, string> placeOf,
        CancellationToken cancellationToken)
    {
        var failures = new List<FieldFailure>();
        var inThisSave = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (row, rules) in rows)
        {
            Func<string, object?> value = name => valueOf(row, name);
            var key = row.State == EntityState.Modified ? KeyOf(row) : null;

            foreach (var rule in rules)
            {
                // Two rows of one save can break a rule between them, and neither is in the database to be found.
                var values = rule.Properties.Select(p => value(p.Name)).ToList();
                var twice = !values.Contains(null) && DeclaredUniqueRules.Kept(rule, value) &&
                            !inThisSave.Add(rule.GetDatabaseName() + "\u001f" + string.Join('\u001f', values));

                if (twice || await DeclaredUniqueRules.TakenAsync(context, row.Metadata, rule, value, key, cancellationToken, rowValueOf: value)
                        .ConfigureAwait(false))
                {
                    failures.Add(UniqueViolation.FailureOf(rule, [placeOf(row)]));
                }
            }
        }

        return failures;
    }

    // A new row touches every rule; a changed one those whose columns it changes — any of them, for a filtered rule.
    private static bool Touches(EntityEntry entry, IIndex rule) =>
        entry.State == EntityState.Added ||
        (FilteredRows.Of(rule) is null
            ? rule.Properties.Any(p => Changed(entry.Property(p.Name)))
            : entry.Properties.Any(Changed));

    private static bool Changed(PropertyEntry property) =>
        property.IsModified && !Equals(property.OriginalValue, property.CurrentValue);

    private static List<object?>? KeyOf(EntityEntry row) =>
        row.Metadata.FindPrimaryKey()?.Properties.Select(p => row.Property(p.Name).CurrentValue).ToList();
}
