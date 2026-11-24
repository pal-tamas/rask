using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Postgres;

/// <summary>
///     The collation <c>x.Loosely()</c> compares by on PostgreSQL: <see cref="Name" />, a nondeterministic ICU collation of
///     no one language that ignores case, accents, kana and width — what <c>Latin1_General_100_CI_AI</c> is on SQL Server.
/// </summary>
/// <remarks>
///     <para>
///         No column is created with it, so no migration operation would name it: it is created, <c>IF NOT EXISTS</c>,
///         at the start of every migration that changes anything — the first one, and an older database's next one. A
///         database migrated before it existed gets it from its next migration; until then a query that asks for it is
///         refused with <c>collation "rask_search" does not exist</c>.
///     </para>
///     <para>
///         <c>LIKE</c> — what <c>Contains</c>, <c>StartsWith</c> and <c>EndsWith</c> are written as — matches by a
///         nondeterministic collation from PostgreSQL 18 on; an older server refuses such a query.
///     </para>
/// </remarks>
internal static class PostgresSearchCollation
{
    /// <summary>The collation's name in the schema and the SQL.</summary>
    internal const string Name = "rask_search";

    /// <summary>The statement that creates it.</summary>
    internal const string Create =
        $"CREATE COLLATION IF NOT EXISTS {Name} (PROVIDER = icu, LOCALE = 'und-u-ks-level1', DETERMINISTIC = false);";

    /// <summary>Whether a migration of <paramref name="operations" /> creates it: any that changes anything.</summary>
    internal static bool CreatedBy(IReadOnlyList<MigrationOperation> operations) =>
        operations.Count > 0;
}
