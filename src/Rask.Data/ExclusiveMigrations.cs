using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Rask.Data;

/// <summary>
///     The migrations no older version of the app may run beside: one that rewrites what rows hold, so an instance still
///     on the old code would misread every row it touches from then on.
/// </summary>
/// <remarks>
///     Marked by the first line of SQL Rask writes into such a migration, so the mark is in the migration's own C# —
///     <c>rask deploy</c> asks the new image which of its pending migrations carry it (<c>rask db pending</c>) and stops
///     the old instance before the new one migrates, instead of running both side by side.
/// </remarks>
internal static class ExclusiveMigrations
{
    /// <summary>The comment that opens the SQL of an exclusive migration's operation.</summary>
    internal const string Prefix = "-- rask:exclusive";

    /// <summary>Whether <paramref name="migration" /> must run with no older version of the app beside it.</summary>
    internal static bool IsExclusive(Migration migration) =>
        migration.UpOperations
            .OfType<SqlOperation>()
            .Any(static o => o.Sql.StartsWith(Prefix, StringComparison.Ordinal));
}
