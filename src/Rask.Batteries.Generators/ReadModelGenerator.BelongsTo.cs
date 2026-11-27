using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// What a BelongsTo<X> reads: X's id, and X's row through its read face — one, a projection of one, or many at once.
public sealed partial class ReadModelGenerator
{
    // The aggregates a BelongsTo in this compilation refers to: only those get the members, so a package whose aggregates
    // nothing refers to publishes none.
    private static HashSet<string> ReferredByBelongsTo(IEnumerable<ReadShape> shapes) =>
        new(
            shapes.SelectMany(static s => s.Relationships)
                .Where(static r => r.Navigation is not null)
                .Select(static r => r.PrincipalTypeName),
            StringComparer.Ordinal);

    private static void AppendBelongsToReads(StringBuilder s, ReadShape shape, string read, string open)
    {
        var reference = "global::Rask.Data.BelongsTo<" + shape.SourceTypeName + ">";
        var id = shape.IdTypeName;

        s.AppendLine();
        s.Append("    extension(").Append(reference).AppendLine(" reference)");
        s.AppendLine("    {");
        s.Append("        /// <summary>The id of the <see cref=\"").Append(shape.SourceTypeName).AppendLine("\" /> referred to.</summary>");
        s.Append("        public ").Append(id).Append(" Id => (").Append(id).AppendLine(")reference.Key;");
        s.AppendLine("    }");
        s.AppendLine();

        // On the nullable reference, so an optional one reads as a required one does: `await fueling.Vehicle.Read()`.
        s.Append("    extension(").Append(reference).AppendLine("? reference)");
        s.AppendLine("    {");
        s.AppendLine("        /// <summary>The row referred to, read through its read face; null for no reference, or a row that is gone or another tenant's.</summary>");
        s.Append("        public global::System.Threading.Tasks.Task<").Append(read).Append("?> Read(").Append(Token).AppendLine(")");
        s.AppendLine("        {");
        s.AppendLine("            if (reference is null)");
        s.AppendLine("            {");
        s.Append("                return global::System.Threading.Tasks.Task.FromResult<").Append(read).AppendLine("?>(null);");
        s.AppendLine("            }");
        s.AppendLine();
        s.Append("            var id = (").Append(id).AppendLine(")reference.Key;");
        s.Append("            return ").Append(open).AppendLine(".FirstOrDefault(row => row.Id == id, cancellationToken);");
        s.AppendLine("        }");
        s.AppendLine();
        s.AppendLine("        /// <summary>What <paramref name=\"selector\" /> picks of the row referred to; the default for no reference, or a row that is gone or another tenant's.</summary>");
        s.Append("        public global::System.Threading.Tasks.Task<TResult?> Read<TResult>(global::System.Linq.Expressions.Expression<global::System.Func<")
            .Append(read).Append(", TResult>> selector, ").Append(Token).AppendLine(")");
        s.AppendLine("        {");
        s.AppendLine("            if (reference is null)");
        s.AppendLine("            {");
        s.AppendLine("                return global::System.Threading.Tasks.Task.FromResult<TResult?>(default);");
        s.AppendLine("            }");
        s.AppendLine();
        s.Append("            var id = (").Append(id).AppendLine(")reference.Key;");
        s.Append("            return ").Append(open).AppendLine(".Where(row => row.Id == id).Select(selector).FirstOrDefault(cancellationToken);");
        s.AppendLine("        }");
        s.AppendLine("    }");
        AppendBatchedRead(s, reference, id, read, open);
    }

    // Many references read at once: one query for every row they name, by id.
    private static void AppendBatchedRead(StringBuilder s, string reference, string id, string read, string open)
    {
        s.AppendLine();
        s.Append("    extension(global::System.Collections.Generic.IEnumerable<").Append(reference).AppendLine("?> references)");
        s.AppendLine("    {");
        s.AppendLine("        /// <summary>");
        s.AppendLine("        ///     The rows referred to, by id, read in one query rather than one per reference. A null reference, or one to a");
        s.AppendLine("        ///     row that is gone or another tenant's, has no entry.");
        s.AppendLine("        /// </summary>");
        s.Append("        public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyDictionary<").Append(id)
            .Append(", ").Append(read).Append(">> Read(").Append(Token).AppendLine(")");
        s.AppendLine("        {");
        s.Append("            var ids = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Distinct(global::System.Linq.Enumerable.Select(")
            .Append("global::System.Linq.Enumerable.OfType<").Append(reference).Append(">(references), static r => (").Append(id)
            .AppendLine(")r.Key)));");
        s.AppendLine("            if (ids.Count == 0)");
        s.AppendLine("            {");
        s.Append("                return new global::System.Collections.Generic.Dictionary<").Append(id).Append(", ").Append(read).AppendLine(">();");
        s.AppendLine("            }");
        s.AppendLine();
        s.Append("            var rows = await ").Append(open)
            .AppendLine(".Where(row => ids.Contains(row.Id)).Query(static (q, ct) => global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(q, ct), cancellationToken).ConfigureAwait(false);");
        s.AppendLine("            return global::System.Linq.Enumerable.ToDictionary(rows, static row => row.Id);");
        s.AppendLine("        }");
        s.AppendLine("    }");
    }
}
