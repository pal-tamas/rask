using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>What <see cref="Db.Lock(string, CancellationToken)" /> takes, in the provider's own words.</summary>
internal static class NamedLock
{
    private const string PostgresLockNotAvailable = "55P03";

    /// <summary>
    ///     Takes every one of <paramref name="names" /> — already distinct and in ordinal order — one after another,
    ///     within one wait; all or none.
    /// </summary>
    internal static async Task<IAsyncDisposable> Take(IReadOnlyList<string> names, TimeSpan? wait, CancellationToken cancellationToken)
    {
        if (names.Count == 0)
        {
            return Nothing.Held;
        }

        var context = Db.CreateOwnContext();
        try
        {
            var provider = context.Database.ProviderName;
            if (string.Equals(provider, ProviderNames.Sqlite, StringComparison.Ordinal))
            {
                // Nothing to hold a connection for: the lock is this process's.
                var database = context.Database.GetConnectionString() + "\n";
                await context.DisposeAsync().ConfigureAwait(false);
                return await ProcessLocks.Take(database, names, wait, cancellationToken).ConfigureAwait(false);
            }

            if (!string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal) &&
                !string.Equals(provider, ProviderNames.Postgres, StringComparison.Ordinal))
            {
                throw new NotSupportedException(
                    $"Db.Lock(\"{names[0]}\") needs a database with named locks: SQL Server, PostgreSQL or SQLite. This app's "
                    + $"database is {provider ?? "unknown"}.");
            }

            return await TakeOnServer(context, string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal), names, wait, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<IAsyncDisposable> TakeOnServer(
        DbContext context, bool onSqlServer, IReadOnlyList<string> names, TimeSpan? wait, CancellationToken cancellationToken)
    {
        if (onSqlServer)
        {
            RefuseTooLong(names);
        }

        // Its own connection, kept for the lock's lifetime — never the one of a Db.Transaction in progress, which
        // would end the lock with the transaction.
        using (AmbientTransaction.Leave())
        {
            await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        var held = new DatabaseLock(context);
        try
        {
            var connection = context.Database.GetDbConnection();
            var deadline = Deadline.Of(wait);
            if (onSqlServer)
            {
                await TakeOnSqlServer(connection, held, names, deadline, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await TakeOnPostgres(connection, held, names, deadline, cancellationToken).ConfigureAwait(false);
            }

            return held;
        }
        catch
        {
            // A wait cancelled at the moment it was granted still holds the lock: let go of every one either way.
            await held.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static TimeoutException TimedOut(string name, TimeSpan? wait) =>
        new($"Db.Lock(\"{name}\") waited {wait?.ToString("c", CultureInfo.InvariantCulture) ?? "without limit"} "
            + "and another holder still had it. Wait longer, or try again once that work is done.");

    private static void RefuseTooLong(IReadOnlyList<string> names)
    {
        if (names.FirstOrDefault(static name => name.Length > SqlServerAppLock.MaxNameLength) is { } name)
        {
            throw new ArgumentException(
                $"A lock's name is at most {SqlServerAppLock.MaxNameLength} characters on SQL Server; this one has {name.Length}.",
                nameof(names));
        }
    }

    private static async Task TakeOnSqlServer(
        DbConnection connection, DatabaseLock held, IReadOnlyList<string> names, Deadline deadline, CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            var left = deadline.Left;
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = SqlServerAppLock.TakeForSession;
                command.Parameters.Add(Parameter(command, "@name", name));
                command.Parameters.Add(Parameter(command, "@wait", left is { } span ? Milliseconds(span) : -1));

                // The wait is sp_getapplock's; the command outlasts it, and waits without end when the lock does.
                command.CommandTimeout = left is { } limit ? (int)Math.Min(int.MaxValue, limit.TotalSeconds + 30) : 0;

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
                    throw TimedOut(name, deadline.Wait);
                }

                if (granted < 0)
                {
                    throw new InvalidOperationException(
                        $"Db.Lock(\"{name}\") was refused by SQL Server's sp_getapplock, which answered {granted} (-2 cancelled, "
                        + "-3 chosen as a deadlock victim, -999 a bad call).");
                }

                held.Taken(SqlServerAppLock.ReleaseForSession, name);
            }
        }
    }

    // PostgreSQL locks the name's hashtext, so that is the order the locks are taken in: two names that hash alike are one
    // lock there, and taking them by name could put one caller's second lock before another's first.
    private static async Task TakeOnPostgres(
        DbConnection connection, DatabaseLock held, IReadOnlyList<string> names, Deadline deadline, CancellationToken cancellationToken)
    {
        var keys = new SortedDictionary<int, string>();
        foreach (var name in names)
        {
            var key = Convert.ToInt32(await Scalar(connection, "SELECT hashtext(@name);", ("@name", name), cancellationToken)
                .ConfigureAwait(false), CultureInfo.InvariantCulture);
            keys.TryAdd(key, name);
        }

        if (deadline.Wait == TimeSpan.Zero)
        {
            foreach (var (key, name) in keys)
            {
                if (await Scalar(connection, "SELECT pg_try_advisory_lock(@key);", ("@key", key), cancellationToken).ConfigureAwait(false) is not true)
                {
                    throw TimedOut(name, deadline.Wait);
                }

                held.Taken(PostgresRelease, key);
            }

            return;
        }

        // lock_timeout bounds each wait (0 is none), and is put back as it was: the session settings may have set it.
        var previous = (string?)await Scalar(connection, "SELECT current_setting('lock_timeout');", null, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            foreach (var (key, name) in keys)
            {
                // A wait that has run out is one more try: lock_timeout 0 would wait without end.
                var left = deadline.Left;
                var timeout = left is { } span ? Math.Max(1, Milliseconds(span)).ToString(CultureInfo.InvariantCulture) : "0";
                try
                {
                    await Scalar(connection, "SELECT set_config('lock_timeout', @timeout, false);", ("@timeout", timeout), cancellationToken)
                        .ConfigureAwait(false);
                    await Scalar(connection, "SELECT pg_advisory_lock(@key);", ("@key", key), cancellationToken).ConfigureAwait(false);
                }
                catch (DbException failure) when (string.Equals(failure.SqlState, PostgresLockNotAvailable, StringComparison.Ordinal))
                {
                    throw TimedOut(name, deadline.Wait);
                }

                held.Taken(PostgresRelease, key);
            }
        }
        finally
        {
            await Restore(connection, previous).ConfigureAwait(false);
        }
    }

    private const string PostgresRelease = "SELECT pg_advisory_unlock(@key);";

    private static async Task Restore(DbConnection connection, string? previous)
    {
        try
        {
            await Scalar(connection, "SELECT set_config('lock_timeout', @timeout, false);", ("@timeout", previous ?? "0"), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (DbException)
        {
            // A connection that cannot take a SET is broken, and goes with the lock.
        }
    }

    private static async Task<object?> Scalar(
        DbConnection connection, string sql, (string Name, object Value)? parameter, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = sql;
            command.CommandTimeout = 0;
            if (parameter is var (name, value))
            {
                command.Parameters.Add(Parameter(command, name, value));
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

    /// <summary>One wait shared by every name of a call: what is left of it when the next one is asked for.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Deadline
    {
        private readonly long _started;

        private Deadline(TimeSpan? wait)
        {
            Wait = wait;
            _started = Stopwatch.GetTimestamp();
        }

        /// <summary>The whole wait the call was given; null is without limit.</summary>
        internal TimeSpan? Wait { get; }

        /// <summary>What is left of it, never below zero; null is without limit.</summary>
        internal TimeSpan? Left =>
            Wait is { } wait ? TimeSpan.FromTicks(Math.Max(0, (wait - Stopwatch.GetElapsedTime(_started)).Ticks)) : null;

        internal static Deadline Of(TimeSpan? wait) => new(wait);
    }

    /// <summary>What a call naming no lock holds.</summary>
    private sealed class Nothing : IAsyncDisposable
    {
        internal static readonly Nothing Held = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The locks the database holds for the session of <paramref name="context" />'s connection.</summary>
    private sealed class DatabaseLock(DbContext context) : IAsyncDisposable
    {
        private readonly List<(string Release, string Parameter, object Value)> _taken = [];
        private int _released;

        internal void Taken(string release, string name) => _taken.Add((release, "@name", name));

        internal void Taken(string release, int key) => _taken.Add((release, "@key", key));

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
            {
                return;
            }

            // Let go by name, the last taken first, before the connection goes back to the pool — which keeps the session,
            // and the locks with it, until the connection is used again.
            try
            {
                for (var i = _taken.Count - 1; i >= 0; i--)
                {
                    var (release, parameter, value) = _taken[i];
                    await Scalar(context.Database.GetDbConnection(), release, (parameter, value), CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (DbException)
            {
                // The connection is gone, and the locks with it.
            }

            await context.DisposeAsync().ConfigureAwait(false);
        }
    }
}
