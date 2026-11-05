using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Rask.Server;

/// <summary>
///     Writes a context's baseline down as applied on a database whose schema predates its migrations — what
///     <see cref="SchemaOwnership" /> describes — before the migrations after it run.
/// </summary>
/// <remarks>
///     Under EF Core's own migration lock (<see cref="IHistoryRepository.AcquireDatabaseLockAsync" />:
///     <c>sp_getapplock</c> on SQL Server, a lock table on SQLite, a table lock in a transaction on PostgreSQL), so two hosts
///     starting together cannot both write the baseline down. Released before <c>Migrate</c>, which takes the same
///     lock itself.
/// </remarks>
internal static class SchemaTakeover
{
    /// <summary>Takes the database over if <paramref name="ownership" /> says it already has the schema.</summary>
    /// <returns>The baseline written down, or null when there was nothing to take over.</returns>
    public static async Task<string?> Run(DbContext db, SchemaOwnership ownership, CancellationToken cancellation)
    {
        // No database yet: there is nothing to take over, only to make.
        if (!ownership.TakesOver
            || !await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellation).ConfigureAwait(false))
        {
            return null;
        }

        var baseline = db.Database.GetMigrations().First();
        var history = db.GetService<IHistoryRepository>();

        // Open across the lock: on SQL Server it belongs to the connection, and is let go with it.
        await db.Database.OpenConnectionAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (history.LockReleaseBehavior != LockReleaseBehavior.Transaction)
            {
                return await WriteBaselineUnderLock(db, ownership, history, baseline, cancellation).ConfigureAwait(false)
                    ? baseline
                    : null;
            }

            // A provider whose lock lasts as long as a transaction (PostgreSQL's) gets one to hold it in.
            var transaction = await db.Database.BeginTransactionAsync(cancellation).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var written = await WriteBaselineUnderLock(db, ownership, history, baseline, cancellation).ConfigureAwait(false);
                await transaction.CommitAsync(cancellation).ConfigureAwait(false);
                return written ? baseline : null;
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Writes <paramref name="baseline" /> down as applied, under the lock, if the database has the schema.</summary>
    private static async Task<bool> WriteBaselineUnderLock(
        DbContext db, SchemaOwnership ownership, IHistoryRepository history, string baseline, CancellationToken cancellation)
    {
        var turn = await history.AcquireDatabaseLockAsync(cancellation).ConfigureAwait(false);
        await using (turn.ConfigureAwait(false))
        {
            var applied = (await history.GetAppliedMigrationsAsync(cancellation).ConfigureAwait(false))
                .Select(row => row.MigrationId)
                .ToList();
            if (applied.Contains(baseline, StringComparer.Ordinal)
                || !await HasSchema(db, ownership, applied, cancellation).ConfigureAwait(false))
            {
                return false;
            }

            await history.CreateIfNotExistsAsync(cancellation).ConfigureAwait(false);
            await db.Database
                .ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(baseline, ProductInfo.GetVersion())), cancellation)
                .ConfigureAwait(false);
            return true;
        }
    }

    /// <summary>Whether the database already has the schema the baseline makes.</summary>
    private static async Task<bool> HasSchema(
        DbContext db, SchemaOwnership ownership, List<string> applied, CancellationToken cancellation)
    {
        var predecessorApplied = applied.Exists(ownership.IsPredecessor);
        if (ownership.Table is not { } table)
        {
            return predecessorApplied;
        }

        if (!await TableExists(db, table, cancellation).ConfigureAwait(false))
        {
            return false;
        }

        // The table without the old set's last migration: some state the baseline does not describe.
        if (ownership.Predecessor is { } predecessor && !predecessorApplied)
        {
            throw new InvalidOperationException(
                $"The database has a {table} table, but its migration history does not show {predecessor} applied, so "
                + $"its schema is not the one {db.GetType().Name}'s first migration starts from, and it was not taken "
                + "over. Bring the database up to the old migrations first, or drop TakesOverFrom(…) from "
                + "OwnsSchema(s => …) if the table alone proves the schema.");
        }

        return true;
    }

    private static async Task<bool> TableExists(DbContext db, string table, CancellationToken cancellation)
    {
        var connection = db.Database.GetDbConnection();
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = string.Equals(
                db.Database.ProviderName, RaskDatabase.EfProviderName(RaskDatabaseProvider.Sqlite), StringComparison.Ordinal)
                ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table"

                // SQL Server, PostgreSQL and every other provider with the standard catalog. Upper case, as SQL Server
                // spells it, which PostgreSQL folds to its own.
                : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @table";
            command.Parameters.Add(Parameter(command, table));

            var count = await command.ExecuteScalarAsync(cancellation).ConfigureAwait(false);
            return Convert.ToInt64(count, CultureInfo.InvariantCulture) > 0;
        }
    }

    private static DbParameter Parameter(DbCommand command, string table)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        return parameter;
    }
}
