using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>
///     Puts a connection back to the database's default isolation once a transaction begun at another level has ended,
///     before it goes back to the pool.
/// </summary>
/// <remarks>
///     <para>
///         SQL Server keeps a <c>SET TRANSACTION ISOLATION LEVEL</c> on the session after the transaction ends, and the
///         pool's <c>sp_reset_connection</c> does not clear it: the next read that draws the connection would run
///         serializable, take range locks, lose read-committed snapshot and become a deadlock victim.
///     </para>
///     <para>
///         PostgreSQL needs nothing (Npgsql sends the level on the <c>BEGIN</c>, which binds that transaction alone),
///         nor does SQLite (Microsoft.Data.Sqlite turns <c>read_uncommitted</c> back off when the transaction completes).
///     </para>
/// </remarks>
internal static class IsolationReset
{
    // Read committed is SQL Server's default; read-committed snapshot then applies again as the database configures it.
    private const string SqlServerDefault = "SET TRANSACTION ISOLATION LEVEL READ COMMITTED";

    /// <summary>
    ///     The statement that puts <paramref name="context" />'s connection back after a transaction at
    ///     <paramref name="isolation" />, or <see langword="null" /> when nothing lingers — no level asked, the default
    ///     asked, or a database that does not keep it.
    /// </summary>
    internal static string? After(DbContext context, IsolationLevel? isolation) =>
        isolation is { } level && level is not (IsolationLevel.ReadCommitted or IsolationLevel.Unspecified) &&
        string.Equals(context.Database.ProviderName, ProviderNames.SqlServer, StringComparison.Ordinal)
            ? SqlServerDefault
            : null;

    /// <summary>
    ///     Sends <paramref name="statement" /> on <paramref name="context" />'s connection, the transaction over. Never
    ///     cancelled — a cancelled block is exactly one whose connection must not go back to the pool as it was — and
    ///     never thrown from: it runs on the way out of a commit that already happened or of a failure that must reach
    ///     the caller, and a connection too broken to take it is one the pool throws away.
    /// </summary>
    internal static async Task Run(DbContext context, string statement)
    {
        if (context.Database.GetDbConnection().State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            await context.Database.ExecuteSqlRawAsync(statement, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DbException)
        {
            // The connection failed a SET: it is broken, and the pool discards a broken connection.
        }
    }
}
