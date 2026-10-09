using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Cqrs;

namespace Rask.Data;

/// <summary>
///     Recognises a provider's "unique index violated" error and finds the index it is about.
/// </summary>
/// <remarks>
///     <para>
///         By the provider's own code first, never by the text of a message that a server may localise:
///         SQLSTATE <c>23505</c> on PostgreSQL, error <c>2601</c> / <c>2627</c> on SQL Server (which SqlClient
///         files under <c>Data["HelpLink.EvtID"]</c>), and SQLite's fixed, unlocalised
///         <c>UNIQUE constraint failed</c>. Nothing here references a provider package, so it works under a
///         plain <c>UseSqlServer</c> as it does under <c>UseRaskSqlServer</c>.
///     </para>
///     <para>
///         The index is then found by NAME — the constraint PostgreSQL reports, the quoted identifier in SQL
///         Server's message — or, on SQLite, which names no index, by the table and columns it lists. The
///         conflicting VALUE, which SQL Server appends to its message, is never read.
///     </para>
/// </remarks>
internal static class UniqueViolation
{
    /// <summary>The index annotation <c>HasViolationMessage</c> writes.</summary>
    internal const string Annotation = "Rask:ViolationMessage";

    private const string PostgresUniqueViolation = "23505";
    private const string SqlServerErrorNumber = "HelpLink.EvtID";
    private const string PostgresConstraintName = "ConstraintName";
    private const string SqliteMarker = "UNIQUE constraint failed: ";

    private static readonly string[] SqlServerUniqueViolations = ["2601", "2627"];
    private static readonly char[] Quotes = ['\'', '"'];

    /// <summary>
    ///     The validation failure <paramref name="failure" /> stands for, when it is the violation of a unique
    ///     index that carries a message; otherwise null, and the failure stays as it is.
    /// </summary>
    internal static RaskValidationException? Translate(DbContext? context, Exception? failure)
    {
        if (context is null ||
            failure is not DbUpdateException { InnerException: DbException provider } ||
            !IsUniqueViolation(provider))
        {
            return null;
        }

        var candidates = context.Model.GetEntityTypes()
            .SelectMany(static type => type.GetIndexes())
            .Where(static index => index.IsUnique && index.FindAnnotation(Annotation)?.Value is string)
            .ToList();

        return Violated(provider, candidates) is { } index
            ? new RaskValidationException(
                [new RequestValidationError(FieldOf(index), (string)index.FindAnnotation(Annotation)!.Value!)],
                failure)
            : null;
    }

    private static bool IsUniqueViolation(DbException provider) =>
        string.Equals(provider.SqlState, PostgresUniqueViolation, StringComparison.Ordinal) ||
        SqlServerUniqueViolations.Contains(provider.Data[SqlServerErrorNumber]?.ToString(), StringComparer.Ordinal) ||
        provider.Message.Contains(SqliteMarker, StringComparison.Ordinal);

    private static IIndex? Violated(DbException provider, List<IIndex> candidates)
    {
        if (provider.Data[PostgresConstraintName] is string constraint)
        {
            return candidates.Find(index => Named(index, constraint));
        }

        var marker = provider.Message.IndexOf(SqliteMarker, StringComparison.Ordinal);
        if (marker >= 0)
        {
            var columns = SqliteColumns(provider.Message, marker + SqliteMarker.Length);
            return candidates.Find(index => string.Equals(ColumnsOf(index), columns, StringComparison.Ordinal));
        }

        // The first quoted identifier that is one of OUR index names. The object and the index are named before
        // the duplicate key value, so a value that happens to spell an index name is never reached.
        foreach (var quoted in provider.Message.Split(Quotes))
        {
            if (candidates.Find(index => Named(index, quoted)) is { } index)
            {
                return index;
            }
        }

        return null;
    }

    private static bool Named(IIndex index, string name) =>
        string.Equals(index.GetDatabaseName(), name, StringComparison.Ordinal);

    // "…: 'UNIQUE constraint failed: Destinations.Name, Destinations.TenantId'." → "Destinations.Name, Destinations.TenantId"
    private static string SqliteColumns(string message, int start)
    {
        var end = message.IndexOf('\'', start);
        return end < 0 ? message[start..] : message[start..end];
    }

    private static string ColumnsOf(IIndex index)
    {
        var table = index.DeclaringEntityType.GetTableName();
        var store = StoreObjectIdentifier.Create(index.DeclaringEntityType, StoreObjectType.Table);

        return string.Join(
            ", ",
            index.Properties.Select(p => $"{table}.{(store is { } at ? p.GetColumnName(at) : p.GetColumnName())}"));
    }

    // The property the index is over, when it is one property beside the tenant: the message then belongs under
    // that field. Several properties are a rule about the row, which is filed under the empty key.
    private static string FieldOf(IIndex index)
    {
        var own = index.Properties.Where(static p => !string.Equals(p.Name, Columns.TenantId, StringComparison.Ordinal)).ToList();
        return own.Count == 1 ? own[0].Name : string.Empty;
    }
}
