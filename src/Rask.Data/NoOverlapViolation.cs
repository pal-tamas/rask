using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     Recognises the refusal of a <c>b.NoOverlap(…)</c> rule on each provider, and answers it with the rule's message
///     under its field — the failure a form shows, as <c>IsUnique("…")</c>'s is.
/// </summary>
/// <remarks>
///     By what the database itself says, never by a localised text: PostgreSQL's SQLSTATE <c>23P01</c> and the
///     constraint it names, SQL Server's error <see cref="NoOverlapSql.SqlServerError" /> whose message is the rule's name
///     (the trigger's own <c>THROW</c>), and the <c>RAISE</c> text SQLite's trigger writes. Nothing here references a
///     provider package.
/// </remarks>
internal static class NoOverlapViolation
{
    private const string PostgresExclusionViolation = "23P01";
    private const string PostgresConstraintName = "ConstraintName";
    private const string SqlServerErrorNumber = "HelpLink.EvtID";
    private const string SqliteMarker = "rask no overlap: ";

    /// <summary>The validation failure a failed save stands for, when a rule refused it; otherwise null.</summary>
    internal static RaskValidationException? Translate(DbContext? context, Exception? failure)
    {
        if (context is null || failure is not DbUpdateException { InnerException: DbException provider } update ||
            Violated(context, provider) is not { } violated)
        {
            return null;
        }

        var rows = update.Entries
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified && violated.Entity.IsAssignableFrom(entry.Metadata))
            .Select(entry => RowPath.Of(context, entry))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new RaskValidationException([FailureOf(violated.Rule, rows.Count == 0 ? [""] : rows)], failure);
    }

    /// <summary>The same for an <c>ExecuteUpdate</c> the database refused: no entries, so under the root's field.</summary>
    internal static RaskValidationException? TranslateCommand(DbContext? context, Exception? failure) =>
        context is not null && failure is DbException provider && Violated(context, provider) is { } violated
            ? new RaskValidationException([FailureOf(violated.Rule, [""])], failure)
            : null;

    /// <summary>The failure <paramref name="rule" /> answers with, under each of <paramref name="rows" />.</summary>
    internal static FieldFailure FailureOf(NoOverlapRule rule, IReadOnlyList<string> rows) =>
        new(rule.Message, [.. rows.Select(row => row + rule.Field)], Source: rule.Name);

    private static (IEntityType Entity, NoOverlapRule Rule)? Violated(DbContext context, DbException provider)
    {
        if (Named(provider) is not { } name)
        {
            return null;
        }

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            foreach (var rule in NoOverlapRule.Of(entityType))
            {
                if (string.Equals(rule.Name, name, StringComparison.Ordinal))
                {
                    return (entityType, rule);
                }
            }
        }

        return null;
    }

    // The rule's name, as the provider reports the refusal — or null for any other failure.
    private static string? Named(DbException provider)
    {
        if (string.Equals(provider.SqlState, PostgresExclusionViolation, StringComparison.Ordinal))
        {
            return provider.Data[PostgresConstraintName] as string;
        }

        if (string.Equals(provider.Data[SqlServerErrorNumber]?.ToString(), NoOverlapSql.SqlServerError.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            // SqlException joins every error of the batch, one a line: the trigger's THROW is the first.
            return provider.Message.Split('\n', 2)[0].Trim();
        }

        var at = provider.Message.IndexOf(SqliteMarker, StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        // Microsoft.Data.Sqlite wraps the RAISE text as "SQLite Error 19: '<text>'.": the name runs to the closing quote.
        var start = at + SqliteMarker.Length;
        var end = provider.Message.IndexOf('\'', start);
        return end < 0 ? provider.Message[start..].Trim() : provider.Message[start..end];
    }
}
