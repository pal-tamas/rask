using Rask.Wire;

namespace Rask.Data;

public static partial class Db
{
    /// <summary>
    ///     Takes the lock named <paramref name="name" /> in the app's database, held until the returned handle is
    ///     disposed — so while one host of the app holds it, no other host, and no other caller on this one, gets it.
    /// </summary>
    /// <param name="name">What is locked, e.g. <c>"pump-" + pumpId</c>. On SQL Server at most 255 characters.</param>
    /// <param name="wait">
    ///     How long to wait for another holder to let go; <see langword="null" /> waits as long as it takes,
    ///     <see cref="TimeSpan.Zero" /> not at all.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait; unset, the work this call belongs to does.</param>
    /// <returns>The held lock. Disposing it lets go; disposing it again does nothing.</returns>
    /// <exception cref="ArgumentException"><paramref name="name" /> is blank, or too long for SQL Server.</exception>
    /// <exception cref="TimeoutException">Another holder still had the lock when <paramref name="wait" /> ran out.</exception>
    /// <exception cref="NotSupportedException">The app's database has no named locks Rask knows.</exception>
    /// <remarks>
    ///     <code>
    /// await using var turn = await Db.Lock("pump-" + pumpId, wait: 2.Minutes);
    /// await pump.Talk();
    ///     </code>
    ///     <para>
    ///         The lock is the database's, on a connection of its own that it keeps for as long as it is held — never the
    ///         connection of a <c>Db.Transaction</c> in progress, so it outlives that transaction's commit or rollback,
    ///         and a host that dies gives it back with its connection. SQL Server takes it with <c>sp_getapplock</c>
    ///         (owned by the session), PostgreSQL with <c>pg_advisory_lock</c> on the name's <c>hashtext</c> — so two
    ///         names that hash alike share one lock there.
    ///     </para>
    ///     <para>
    ///         SQLite has no lock across connections, and a SQLite app runs on one host: there the lock is this
    ///         process's own, one per database and name.
    ///     </para>
    /// </remarks>
    public static Task<IAsyncDisposable> Lock(string name, TimeSpan? wait = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (wait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(wait), wait, "A lock's wait is zero or more; null waits without end.");
        }

        return NamedLock.Take(name, wait, Ambient.Or(cancellationToken));
    }
}
