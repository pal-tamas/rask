using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Asks the database which relationship still points at the rows a refused delete was removing — for SQLite, whose
///     <c>FOREIGN KEY constraint failed</c> names none.
/// </summary>
/// <remarks>
///     <para>
///         ONE statement, on the connection and in the transaction the delete ran in, so it sees what the delete saw:
///         <c>SELECT CASE WHEN EXISTS (…) THEN 0 WHEN EXISTS (…) THEN 1 … ELSE -1 END</c>, one cheap <c>EXISTS</c> per
///         relationship over its foreign key's columns, the first that holds a row answering. Identifiers are the
///         model's, quoted by the provider; every value is a parameter.
///     </para>
///     <para>
///         The rows are named either by the keys of the entries a save deleted, or — for <c>ExecuteDelete</c> — by the
///         failed statement's own <c>FROM … WHERE …</c>, run again as the subquery that selects their keys.
///     </para>
/// </remarks>
internal static class ReferenceProbe
{
    // Far under SQLite's 32,766 host parameters; a save that deleted more rows than this is answered as before.
    private const int MostValues = 10_000;

    /// <summary>The relationships a delete of these types can be refused by, those with a sentence first.</summary>
    internal static List<IForeignKey> Refusing(IEnumerable<IEntityType> deleted) =>
        [.. deleted
            .SelectMany(static type => type.GetReferencingForeignKeys())
            .Distinct()
            .Where(static key => key.DeleteBehavior is not (DeleteBehavior.Cascade or DeleteBehavior.SetNull) &&
                                 StoreOf(key.DeclaringEntityType) is not null)
            .OrderBy(static key => ReferencedDelete.SentenceOf(key) is null)];

    /// <summary>The first of <paramref name="keys" /> that still holds a row onto one of <paramref name="deleted" />.</summary>
    /// <returns>The relationship; <see langword="null" /> when none does, or the rows cannot be named.</returns>
    internal static (bool Asked, IForeignKey? Key) Ask(DbContext context, IReadOnlyList<IForeignKey> keys, IReadOnlyList<EntityEntry> deleted)
    {
        var values = deleted.Sum(static entry => entry.Metadata.FindPrimaryKey()?.Properties.Count ?? 0) * keys.Count;
        if (keys.Count == 0 || values > MostValues)
        {
            return (false, null);
        }

        var connection = context.Database.GetDbConnection();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return Ask(context, connection, transaction, keys, (command, key) => Rows(context, command, key, deleted));
    }

    /// <summary>The same for an <c>ExecuteDelete</c>, whose rows are what <paramref name="failed" /> was deleting.</summary>
    internal static (bool Asked, IForeignKey? Key) Ask(
        DbContext context, IReadOnlyList<IForeignKey> keys, DbCommand failed, BulkDelete delete) =>
        keys.Count == 0 || failed.Connection is not { } connection
            ? (false, null)
            : Ask(context, connection, failed.Transaction, keys, (command, key) => Rows(context, command, key, failed, delete));

    private static (bool Asked, IForeignKey? Key) Ask(
        DbContext context,
        DbConnection connection,
        DbTransaction? transaction,
        IReadOnlyList<IForeignKey> keys,
        Func<DbCommand, IForeignKey, string?> rows)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var opened = connection.State != ConnectionState.Open;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var text = new StringBuilder("SELECT CASE");
        var asked = 0;
        for (var i = 0; i < keys.Count; i++)
        {
            if (rows(command, keys[i]) is not { } principal)
            {
                continue;
            }

            asked++;
            text.Append(CultureInfo.InvariantCulture, $" WHEN EXISTS (SELECT 1 FROM {Table(sql, keys[i].DeclaringEntityType)} WHERE ")
                .Append(Columns(sql, keys[i].Properties, StoreOf(keys[i].DeclaringEntityType)!.Value, qualifier: null))
                .Append(CultureInfo.InvariantCulture, $" IN ({principal})) THEN {i}");
        }

        if (asked == 0)
        {
            return (false, null);
        }

        text.Append(" ELSE -1 END");

#pragma warning disable CA2100 // identifiers are the model's, quoted by the provider; every value is a parameter
        command.CommandText = text.ToString();
#pragma warning restore CA2100

        try
        {
            if (opened)
            {
                connection.Open();
            }

            var found = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            return (true, found < 0 ? null : keys[found]);
        }
        finally
        {
            if (opened)
            {
                connection.Close();
            }
        }
    }

    // VALUES (@rask_0, @rask_1), (…): the principal key of every deleted entry the relationship points at.
    private static string? Rows(DbContext context, DbCommand command, IForeignKey key, IReadOnlyList<EntityEntry> deleted)
    {
        var sql = context.GetService<ISqlGenerationHelper>();
        var principal = key.PrincipalKey.Properties;
        var rows = new List<string>();

        foreach (var entry in deleted.Where(entry => key.PrincipalEntityType.IsAssignableFrom(entry.Metadata)))
        {
            var row = new List<string>(principal.Count);
            foreach (var property in principal)
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"rask_ref_{command.Parameters.Count}");
                command.Parameters.Add(
                    property.GetRelationalTypeMapping().CreateParameter(command, name, entry.OriginalValues[property], property.IsNullable));
                row.Add(sql.GenerateParameterNamePlaceholder(name));
            }

            rows.Add("(" + string.Join(", ", row) + ")");
        }

        return rows.Count == 0 ? null : "VALUES " + string.Join(", ", rows);
    }

    // SELECT "t"."Id" FROM "Trailer" AS "t" WHERE …: the failed statement, selecting the keys it was deleting.
    private static string? Rows(DbContext context, DbCommand command, IForeignKey key, DbCommand failed, BulkDelete delete)
    {
        if (StoreOf(key.PrincipalEntityType) is not { } store)
        {
            return null;
        }

        // The failed statement's own parameters, as EF Core typed them: only SQLite is asked, and its parameters belong
        // to no one collection, so they are bound again rather than copied (a copy's DbType would retype the value).
        if (command.Parameters.Count == 0)
        {
            foreach (DbParameter parameter in failed.Parameters)
            {
                command.Parameters.Add(parameter);
            }
        }

        var sql = context.GetService<ISqlGenerationHelper>();
        return "SELECT " + Columns(sql, key.PrincipalKey.Properties, store, delete.Qualifier) + " " + delete.From;
    }

    private static string Table(ISqlGenerationHelper sql, IEntityType type) =>
        sql.DelimitIdentifier(type.GetTableName()!, type.GetSchema());

    // "a" or ("a", "b") — a row value, which SQLite compares with IN since 3.15.
    private static string Columns(ISqlGenerationHelper sql, IReadOnlyList<IProperty> properties, StoreObjectIdentifier store, string? qualifier)
    {
        var columns = properties
            .Select(property => (qualifier is null ? "" : qualifier + ".") + sql.DelimitIdentifier(property.GetColumnName(store)!))
            .ToList();

        return columns is [var one] ? one : "(" + string.Join(", ", columns) + ")";
    }

    private static StoreObjectIdentifier? StoreOf(IEntityType type) =>
        StoreObjectIdentifier.Create(type, StoreObjectType.Table);
}
