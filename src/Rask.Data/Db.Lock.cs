namespace Rask.Data;

public static partial class Db
{
    /// <summary>
    ///     The lock named <paramref name="name" /> in the app's database, taken when awaited and held until the returned
    ///     handle is disposed — so while one host of the app holds it, no other host, and no other caller on this one, gets
    ///     it: <c>await using var turn = await Db.Lock("pump:7").For(30.Seconds);</c>
    /// </summary>
    /// <param name="name">What is locked, e.g. <c>"pump:" + pumpId</c>. On SQL Server at most 255 characters.</param>
    /// <param name="cancellationToken">Cancels the wait; unset, the work this call belongs to does.</param>
    /// <returns>
    ///     The lock still being worded. Awaited as it is, it waits for another holder as long as it takes;
    ///     <c>.For(30.Seconds)</c> waits at most that long and then throws <see cref="TimeoutException" />, and
    ///     <c>.For(TimeSpan.Zero)</c> does not wait at all. Awaiting it hands back the held lock: disposing that lets go,
    ///     and disposing it again does nothing.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="name" /> is blank, or too long for SQL Server.</exception>
    /// <remarks>
    ///     <code>
    /// await using var turn = await Db.Lock("pump:" + pumpId).For(2.Minutes);
    /// await pump.Talk();
    ///     </code>
    ///     <para>
    ///         Awaiting it throws <see cref="TimeoutException" /> when another holder still has the lock once the wait
    ///         runs out, and <see cref="NotSupportedException" /> on a database with no named locks Rask knows.
    ///     </para>
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
    public static Locking Lock(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Locking(name, wait: null, cancellationToken);
    }
}
