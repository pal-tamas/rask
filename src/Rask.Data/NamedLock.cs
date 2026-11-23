using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>What <see cref="Db.Lock" /> takes, in the provider's own words.</summary>
internal static class NamedLock
{
    private const string PostgresLockNotAvailable = "55P03";

    internal static async Task<IAsyncDisposable> Take(string name, TimeSpan? wait, CancellationToken cancellationToken)
    {
        var context = Db.CreateOwnContext();
        try
        {
            var provider = context.Database.ProviderName;
            if (string.Equals(provider, ProviderNames.Sqlite, StringComparison.Ordinal))
            {
                // Nothing to hold a connection for: the lock is this process's.
                var key = context.Database.GetConnectionString() + "\n" + name;
                await context.DisposeAsync().ConfigureAwait(false);
                return await ProcessLocks.Take(key, name, wait, cancellationToken).ConfigureAwait(false);
            }

            var release = provider switch
            {
                ProviderNames.SqlServer => SqlServerReleaseStatement(name),
                ProviderNames.Postgres => "SELECT pg_advisory_unlock(hashtext(@name));",
                _ => throw new NotSupportedException(
                    $"Db.Lock(\"{name}\") needs a database with named locks: SQL Server, PostgreSQL or SQLite. This app's "
                    + $"database is {provider ?? "unknown"}."),
            };

            // Its own connection, kept for the lock's lifetime — never the one of a Db.Transaction in progress, which
            // would end the lock with the transaction.
            using (AmbientTransaction.Leave())
            {
                await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            var held = new DatabaseLock(context, name, release);
            try
            {
                if (string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal))
                {
                    await TakeOnSqlServer(context.Database.GetDbConnection(), name, wait, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await TakeOnPostgres(context.Database.GetDbConnection(), name, wait, cancellationToken).ConfigureAwait(false);
                }

                return held;
            }
            catch
            {
                // A wait cancelled at the moment it was granted still holds the lock: let go of it either way.
                await held.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static TimeoutException TimedOut(string name, TimeSpan? wait) =>
        new($"Db.Lock(\"{name}\") waited {wait?.ToString("c", CultureInfo.InvariantCulture) ?? "without limit"} "
            + "and another holder still had it. Wait longer, or try again once that work is done.");

    private static string SqlServerReleaseStatement(string name)
    {
        if (name.Length > SqlServerAppLock.MaxNameLength)
        {
            throw new ArgumentException(
                $"A lock's name is at most {SqlServerAppLock.MaxNameLength} characters on SQL Server; this one has {name.Length}.",
                nameof(name));
        }

        return SqlServerAppLock.ReleaseForSession;
    }

    private static async Task TakeOnSqlServer(DbConnection connection, string name, TimeSpan? wait, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = SqlServerAppLock.TakeForSession;
            command.Parameters.Add(Parameter(command, "@name", name));
            command.Parameters.Add(Parameter(command, "@wait", wait is { } span ? Milliseconds(span) : -1));

            // The wait is sp_getapplock's; the command outlasts it, and waits without end when the lock does.
            command.CommandTimeout = wait is { } limit ? (int)Math.Min(int.MaxValue, limit.TotalSeconds + 30) : 0;

            int granted;
            try
            {
                granted = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
            }
            catch (DbException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (granted == SqlServerAppLock.TimedOut)
            {
                throw TimedOut(name, wait);
            }

            if (granted < 0)
            {
                throw new InvalidOperationException(
                    $"Db.Lock(\"{name}\") was refused by SQL Server's sp_getapplock, which answered {granted} (-2 cancelled, "
                    + "-3 chosen as a deadlock victim, -999 a bad call).");
            }
        }
    }

    private static async Task TakeOnPostgres(DbConnection connection, string name, TimeSpan? wait, CancellationToken cancellationToken)
    {
        if (wait == TimeSpan.Zero)
        {
            var tried = await Scalar(connection, "SELECT pg_try_advisory_lock(hashtext(@name));", name, null, cancellationToken)
                .ConfigureAwait(false);
            if (tried is not true)
            {
                throw TimedOut(name, wait);
            }

            return;
        }

        // lock_timeout bounds the wait (0 is none), and is put back as it was: the session settings may have set it.
        var previous = (string?)await Scalar(connection, "SELECT current_setting('lock_timeout');", name, null, cancellationToken)
            .ConfigureAwait(false);
        var timeout = wait is { } span ? Milliseconds(span).ToString(CultureInfo.InvariantCulture) : "0";
        try
        {
            await Scalar(connection, "SELECT set_config('lock_timeout', @timeout, false);", name, timeout, cancellationToken)
                .ConfigureAwait(false);
            await Scalar(connection, "SELECT pg_advisory_lock(hashtext(@name));", name, null, cancellationToken).ConfigureAwait(false);
        }
        catch (DbException failure) when (string.Equals(failure.SqlState, PostgresLockNotAvailable, StringComparison.Ordinal))
        {
            throw TimedOut(name, wait);
        }
        finally
        {
            await Restore(connection, previous).ConfigureAwait(false);
        }
    }

    private static async Task Restore(DbConnection connection, string? previous)
    {
        try
        {
            await Scalar(connection, "SELECT set_config('lock_timeout', @timeout, false);", "", previous ?? "0", CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (DbException)
        {
            // A connection that cannot take a SET is broken, and goes with the lock.
        }
    }

    private static async Task<object?> Scalar(
        DbConnection connection, string sql, string name, string? timeout, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            command.CommandTimeout = 0;
            command.Parameters.Add(Parameter(command, "@name", name));
            if (timeout is not null)
            {
                command.Parameters.Add(Parameter(command, "@timeout", timeout));
            }

            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static int Milliseconds(TimeSpan span) => (int)Math.Min(int.MaxValue, Math.Ceiling(span.TotalMilliseconds));

    private static DbParameter Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        return parameter;
    }

    /// <summary>A lock the database holds for the session of <paramref name="context" />'s connection.</summary>
    private sealed class DatabaseLock(DbContext context, string name, string release) : IAsyncDisposable
    {
        private int _released;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            // Let go by name before the connection goes back to the pool, which keeps the session — and the lock with
            // it — until the connection is used again.
            try
            {
                await Scalar(context.Database.GetDbConnection(), release, name, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch (DbException)
            {
                // The connection is gone, and the lock with it.
            }

            await context.DisposeAsync().ConfigureAwait(false);
        }
    }
}
