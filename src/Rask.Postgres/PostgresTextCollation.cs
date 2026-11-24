using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     The SQL that makes the collation <c>OrderTextBy</c> asks for on PostgreSQL: a nondeterministic ICU collation, so
///     <c>=</c>, a unique index, <c>ORDER BY</c> and <c>LIKE</c> on the text columns compare as SQL Server's does.
/// </summary>
/// <remarks>
///     <para>
///         <c>LIKE</c> — what EF writes <c>Contains</c>, <c>StartsWith</c> and <c>EndsWith</c> as — refuses a
///         nondeterministic collation before PostgreSQL 18. So the statement first checks the server and stops the
///         migration there with the reason, rather than leaving every search to fail later.
///     </para>
///     <para>
///         <c>IF NOT EXISTS</c>: a later migration running it again changes nothing, and a database whose collation was
///         made under another locale keeps it — drop it to change it.
///     </para>
/// </remarks>
internal static class PostgresTextCollation
{
    /// <summary>The <c>DO</c> block that checks the server and creates the collation, or null when the model needs none.</summary>
    internal static string? CreateFor(IReadOnlyList<MigrationOperation> operations, IModel? model)
    {
        if (model?.FindAnnotation(TextCollation.Annotation)?.Value is not string name
            || TextCollation.Parse(name).NondeterministicIcuLocale is not { } locale
            || !operations.Any(UsesTheCollation))
        {
            return null;
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
             DO $rask$
             BEGIN
                 IF current_setting('server_version_num')::int < 180000 THEN
                     RAISE EXCEPTION 'c.OrderTextBy("{name}") compares text case-insensitively on PostgreSQL through a nondeterministic collation, and LIKE (Contains, StartsWith, EndsWith) on one needs PostgreSQL 18 or later; this server is %. Upgrade it, or name a case- and accent-sensitive collation.', current_setting('server_version');
                 END IF;
                 CREATE COLLATION IF NOT EXISTS {PostgresTextCollationOptionsExtension.Name} (PROVIDER = icu, LOCALE = '{locale}', DETERMINISTIC = false);
             END
             $rask$;
             """);
    }

    private static bool UsesTheCollation(MigrationOperation operation) =>
        operation switch
        {
            CreateTableOperation create => create.Columns.Exists(Collated),
            AddColumnOperation add => Collated(add),
            AlterColumnOperation alter => Collated(alter),
            _ => false,
        };

    private static bool Collated(ColumnOperation column) =>
        string.Equals(column.Collation, PostgresTextCollationOptionsExtension.Name, StringComparison.Ordinal);
}
