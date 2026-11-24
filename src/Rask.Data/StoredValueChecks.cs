using System.Collections;
using System.Globalization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     The rules of every value object a stored row holds, asked again: a value loaded from a row is kept as stored and
///     never checked (<see cref="RowLoading" />), so only this finds one a rule has been tightened against since — what
///     <c>rask audit</c> adds to the rules a save asks.
/// </summary>
internal static class StoredValueChecks
{
    /// <summary>What a value is told that its rules accept but would keep differently: untrimmed, or not in capitals.</summary>
    internal const string NotAsKept = "Stored differently from how its rules keep it (surrounding blanks, or letters its rules raise to capitals).";

    /// <summary>What the rules of each value object <paramref name="row" /> holds say against it, under <paramref name="path" />.</summary>
    /// <param name="row">The row, tracked.</param>
    /// <param name="path">Where the row sits in its aggregate (<see cref="RowPaths" />): <c>""</c> for the root.</param>
    /// <returns>Each failure under its field: <c>Name</c>, <c>Km.End</c>, <c>Lines[2].Quantity</c>.</returns>
    internal static IEnumerable<FieldFailure> Of(EntityEntry row, string path)
    {
        // A property EF Core converts (a one-value object), and a complex property (one of several values).
        var members = row.Properties.Select(static p => (p.Metadata.Name, p.CurrentValue))
            .Concat(row.ComplexProperties.Select(static c => (c.Metadata.Name, c.CurrentValue)));

        foreach (var (name, value) in members)
        {
            foreach (var failure in Held(value, path + name))
            {
                yield return failure;
            }
        }
    }

    // One value, or each of a collection of them (a JSON column of values), the collection's by its position.
    private static IEnumerable<FieldFailure> Held(object? value, string field)
    {
        switch (value)
        {
            case IStoredValue stored:
                foreach (var failure in Asked(stored, field))
                {
                    yield return failure;
                }

                break;
            case IEnumerable many and not string:
                var index = 0;
                foreach (var item in many)
                {
                    if (item is IStoredValue each)
                    {
                        foreach (var failure in Asked(each, field + "[" + index.ToString(CultureInfo.InvariantCulture) + "]"))
                        {
                            yield return failure;
                        }
                    }

                    index++;
                }

                break;
        }
    }

    private static IEnumerable<FieldFailure> Asked(IStoredValue value, string field)
    {
        var problems = value.Recheck(out var kept);
        foreach (var problem in problems)
        {
            yield return new FieldFailure(problem.Message, [problem.Field is { Length: > 0 } member ? field + "." + member : field]);
        }

        if (problems.Count == 0 && !kept)
        {
            yield return new FieldFailure(NotAsKept, [field]);
        }
    }
}
