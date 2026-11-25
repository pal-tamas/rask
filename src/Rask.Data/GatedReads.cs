using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     A read face's members that carry a gated member of the aggregate behind it (<c>b.Gated(…)</c>): emptied on every row
///     a query hands back while the gate is shut, so a page — and the wire to a browser — never sees what the work in
///     flight may not write.
/// </summary>
/// <remarks>Rows a query projects with <c>Select</c> are its own shape, and are not touched.</remarks>
internal static class GatedReads
{
    private static readonly ConcurrentDictionary<Type, (Type Entity, string Member, string Gated)[]> Columns = new();

    /// <summary>Empties the gated members of <paramref name="result" /> — one row, a list of them, or anything else, left alone.</summary>
    internal static void Hide<[DynamicallyAccessedMembers(DataTrimming.Entity)] TRead>(DbContext context, object? result)
    {
        if (!GatedMembers.Any || result is null)
        {
            return;
        }

        var gated = Columns.GetOrAdd(typeof(TRead), static read => Of(read));
        if (gated.Length == 0)
        {
            return;
        }

        var shut = gated
            .Where(static g => GeneratedGates.IsShut(g.Entity, g.Gated))
            .Select(g => context.Model.FindEntityType(typeof(TRead))?.FindProperty(g.Member))
            .Where(static p => p is { IsNullable: true })
            .ToList();
        if (shut.Count == 0)
        {
            return;
        }

        IEnumerable<TRead> rows = [];
        if (result is TRead one)
        {
            rows = [one];
        }
        else if (result is IEnumerable many)
        {
            rows = many.OfType<TRead>();
        }

        foreach (var row in rows)
        {
            foreach (var property in shut)
            {
                // The member the read face maps, through its setter or, for a get-only one, its backing field.
                if (property!.PropertyInfo is { CanWrite: true } settable)
                {
                    settable.SetValue(row, null);
                }
                else
                {
                    property.FieldInfo?.SetValue(row, null);
                }
            }
        }
    }

    // The read face's members whose write path starts at a gated member: `Card`, or `Card.Value` held as `Card`.
    private static (Type Entity, string Member, string Gated)[] Of(Type read)
    {
        if (ReadModelRegistry.MappingOf(read) is not { } mapping)
        {
            return [];
        }

        var gated = GatedMembers.Of(mapping.WriteType).Select(static g => g.Key).ToList();
        return
        [
            .. mapping.Columns
                .Select(c => (Column: c, Gated: gated.Find(g => string.Equals(c.Path, g, StringComparison.Ordinal) ||
                                                             c.Path.StartsWith(g + ".", StringComparison.Ordinal))))
                .Where(static c => c.Gated is not null)
                .Select(c => (mapping.WriteType, c.Column.Member, c.Gated!)),
        ];
    }
}
