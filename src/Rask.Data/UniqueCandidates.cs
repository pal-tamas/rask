using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     <c>rask audit --unique-candidates</c>: every unique index the model declares, asked of the stored rows as if the
///     database did not have it yet — the groups of rows that hold the same values, which would stop the migration that
///     adds the index. The ids alone, never the values.
/// </summary>
/// <remarks>
///     <para>
///         For an index the app has just declared (<c>HasIndex(…).IsUnique()</c>) and not yet migrated: the audit reads the
///         model, so it answers before the migration is run, and the database has nothing to refuse yet. An index the
///         database already holds answers clean.
///     </para>
///     <para>
///         In LINQ, the same on every provider: each row's key columns and id are read — every row, a deleted one and every
///         tenant's included, as the database's index counts them; a <c>.Where(…)</c> the index declares narrows the rows as
///         the index would — and grouped in memory. A row with a NULL in its key collides with nothing (as on PostgreSQL and
///         SQLite, and on SQL Server through the filter EF Core adds). Two texts that differ only in letter case are reported
///         apart, as a collision under a case-insensitive collation — SQL Server's default — only.
///     </para>
/// </remarks>
internal static class UniqueCandidates
{
    // Between the parts of a composite key: a character no stored text a person typed holds.
    private const char Between = '\u001f';

    private static readonly MethodInfo CheckMethod =
        ((MethodCallExpression)((Expression<Func<Task>>)(() => CheckIndexAsync<object>(null!, null!, null, null!, default))).Body)
        .Method.GetGenericMethodDefinition();

    /// <summary>Asks every unique index of <paramref name="types" />, recording each group of rows that would collide.</summary>
    /// <param name="context">The audit's context.</param>
    /// <param name="types">The entity types whose indexes to ask.</param>
    /// <param name="findings">Where each collision, and each index that cannot be asked, is recorded.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    internal static async Task Check(
        DbContext context, IEnumerable<IEntityType> types, RuleAuditFindings findings, CancellationToken cancellationToken)
    {
        foreach (var type in types.Where(static t => t.GetTableName() is not null && t.FindPrimaryKey() is not null))
        {
            int? read = null;
            foreach (var index in type.GetDeclaredIndexes().Where(static i => i.IsUnique))
            {
                var rows = FilteredRows.Of(index);
                if (!index.Properties.All(static p => p is IReadOnlyProperty))
                {
                    findings.Note($"{type.ClrType.Name} ({Columns(index)}): not asked — it covers a member that is not a column of its own.");
                    continue;
                }

                if (rows is null && index.GetFilter() is { } filter && !OnlyKeysNotNull(filter))
                {
                    findings.Note($"{type.ClrType.Name} ({Columns(index)}): not asked — its filter is SQL (HasFilter); declare it with .Where(…).");
                    continue;
                }

                var rowsRead = await CheckIndex(context, index, rows, findings, cancellationToken).ConfigureAwait(false);
                read = Math.Max(read ?? 0, rowsRead);
            }

            if (read is { } count)
            {
                findings.Read(type.ClrType.Name, count);
            }
        }
    }

    // `[CardNumber] IS NOT NULL`, which EF Core adds to SQL Server's nullable unique index: the NULLs this skips anyway.
    private static bool OnlyKeysNotNull(string filter) =>
        filter.Split(" AND ", StringSplitOptions.TrimEntries).All(static part => part.EndsWith(" IS NOT NULL", StringComparison.OrdinalIgnoreCase));

    private static string Columns(IReadOnlyIndex index) => string.Join(", ", index.Properties.Select(static p => p.Name));

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "The type argument is an entity type of the context's model, which EF Core already roots.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "As above: a mapped entity type, always a reference type.")]
    private static Task<int> CheckIndex(
        DbContext context, IIndex index, LambdaExpression? rows, RuleAuditFindings findings, CancellationToken cancellationToken) =>
        (Task<int>)CheckMethod.MakeGenericMethod(index.DeclaringEntityType.ClrType).Invoke(null, [context, index, rows, findings, cancellationToken])!;

    private static async Task<int> CheckIndexAsync<[DynamicallyAccessedMembers(DataTrimming.Entity)] TEntity>(
        DbContext context, IIndex index, LambdaExpression? rows, RuleAuditFindings findings, CancellationToken cancellationToken)
        where TEntity : class
    {
        var type = index.DeclaringEntityType;
        var keys = index.Properties.Count;
        var exact = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var folded = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var read = 0;

        var query = context.Set<TEntity>().IgnoreQueryFilters().AsNoTracking();
        if (rows is not null)
        {
            query = query.Where((Expression<Func<TEntity, bool>>)rows);
        }

        await foreach (var row in query.Select(Values<TEntity>(index)).AsAsyncEnumerable().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            read++;
            if (row.Take(keys).Any(static v => v is null))
            {
                continue;
            }

            var id = string.Join(", ", row.Skip(keys).Select(Text));
            Add(exact, Key(row, keys, fold: false), id);
            Add(folded, Key(row, keys, fold: true), id);
        }

        var field = Columns(index);
        var collide = $"Would break the unique index on ({field}): rows that hold the same values.";
        var collideIgnoringCase = $"Would break the unique index on ({field}) under a case-insensitive collation (SQL Server's default): "
                                  + "rows that differ only in letter case.";
        var exactGroups = exact.Values.Where(static g => g.Count > 1).ToList();
        foreach (var group in exactGroups)
        {
            findings.Add(type.ClrType.Name, field, collide, string.Join(" = ", group));
        }

        // Only what letter case alone joins: a group the exact grouping already reported is not said twice.
        var reported = exactGroups.Select(static g => string.Join(" = ", g)).ToHashSet(StringComparer.Ordinal);
        foreach (var group in folded.Values.Where(g => g.Count > 1 && !reported.Contains(string.Join(" = ", g))))
        {
            findings.Add(type.ClrType.Name, field, collideIgnoringCase, string.Join(" = ", group));
        }

        return read;
    }

    // row => new object[] { row.Key1, …, row.Id1, … }: the key's columns, then the row's own id.
    private static Expression<Func<TEntity, object?[]>> Values<TEntity>(IReadOnlyIndex index)
    {
        var row = Expression.Parameter(typeof(TEntity), "row");
        var columns = index.Properties.OfType<IReadOnlyProperty>().Concat(index.DeclaringEntityType.FindPrimaryKey()!.Properties)
            .Select(p => (Expression)Expression.Convert(StoredProperty.Of(row, p), typeof(object)));
        return Expression.Lambda<Func<TEntity, object?[]>>(Expression.NewArrayInit(typeof(object), columns), row);
    }

    private static string Key(object?[] row, int keys, bool fold) =>
        string.Join(Between, row.Take(keys).Select(v => v is string text && fold ? text.ToUpperInvariant() : Text(v)));

    private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static void Add(Dictionary<string, List<string>> groups, string key, string id)
    {
        if (!groups.TryGetValue(key, out var ids))
        {
            groups[key] = ids = [];
        }

        ids.Add(id);
    }
}
