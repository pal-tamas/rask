using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Rask.SQLite.Crdt;

/// <summary>
///     Loads the cr-sqlite extension into every connection EF opens, and finalizes it before every close.
/// </summary>
/// <remarks>
///     <para>
///         <b>Every connection, not once at startup.</b> A loadable extension is per connection, and
///         <c>Microsoft.Data.Sqlite</c> pools connections — so a reused handle is a fresh open as far as
///         extensions are concerned. Loading once would work until the pool recycled, and then stop.
///     </para>
///     <para>
///         <b>Finalize before close.</b> cr-sqlite keeps per-connection state; closing without
///         <c>crsql_finalize()</c> leaves it behind. This is also why pooling should be off for a
///         CRDT database — a handle handed back to the pool mid-state and reused elsewhere corrupts
///         quietly rather than failing.
///     </para>
/// </remarks>
public sealed class CrdtConnectionInterceptor(RaskCrdtOptions options) : DbConnectionInterceptor
{
    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        LoadInto(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc />
    public override Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        LoadInto(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        FinalizeOn(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        FinalizeOn(connection);
        return base.ConnectionClosingAsync(connection, eventData, result);
    }

    internal void LoadInto(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite)
        {
            // Loudly, because the alternative is worse than a crash: skipping would leave an app that
            // looks like it works and never replicates anything, and that is discovered as data loss.
            // A wrapping connection (profilers and the like) lands here too, hence naming the type.
            throw new InvalidOperationException(
                $"Rask.SQLite.Crdt needs a {nameof(SqliteConnection)}, but the context opened a " +
                $"{connection.GetType().Name}. cr-sqlite is a SQLite extension; it cannot back another " +
                "provider, and a connection wrapper has to be removed for the extension to be loadable.");
        }

        options.Validate();
        sqlite.EnableExtensions(true);

        // Named explicitly rather than left to SQLite's filename-derived guess: the C half's init is the
        // entry point, and the name derived from "crsqlite.dylib" happens to match only by luck on some
        // platforms.
        sqlite.LoadExtension(options.ExtensionPath, "sqlite3_crsqlite_init");
    }

    internal static void FinalizeOn(DbConnection connection)
    {
        if (connection is not SqliteConnection { State: System.Data.ConnectionState.Open } sqlite)
        {
            return;
        }

        try
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT crsql_finalize();";
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // The extension was never loaded on this connection, or the database is already going away.
            // Neither is worth failing a close over — there is nothing left to finalize either way.
        }
    }
}
