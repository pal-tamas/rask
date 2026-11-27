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
    ///         (owned by the session), PostgreSQL with <c>pg_advisory_lock</c> on the name's 64-bit <c>hashtextextended</c> —
    ///         so two names that hash alike would share one lock there, about once in four billion billion pairs.
    ///     </para>
    ///     <para>
    ///         SQLite has no lock across connections, and a SQLite app runs on one host: there the lock is this
    ///         process's own, one per database and name.
    ///     </para>
    /// </remarks>
    public static Locking Lock(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Locking([name], wait: null, cancellationToken);
    }

    /// <summary>
    ///     Every lock <paramref name="names" /> names, taken together when awaited and held until the returned handle is
    ///     disposed: <c>await using var chips = await Db.Lock(codes.Select(c =&gt; "chip:" + c)).For(30.Seconds);</c>
    /// </summary>
    /// <param name="names">What is locked; a name given twice is taken once, and none at all takes nothing.</param>
    /// <param name="cancellationToken">Cancels the wait; unset, the work this call belongs to does.</param>
    /// <returns>
    ///     The locks still being worded, as <see cref="Lock(string, CancellationToken)" /> hands back one: <c>.For(…)</c>
    ///     bounds the whole wait, not each name's.
    /// </returns>
    /// <exception cref="ArgumentException">A name is blank, or too long for SQL Server.</exception>
    /// <remarks>
    ///     <para>
    ///         The names are taken one after another in ordinal order, whatever order they were given in — so two callers
    ///         that share some of them can never each hold one the other waits for. On PostgreSQL the order is that of
    ///         the names' <c>hashtextextended</c>, the locks it actually takes.
    ///     </para>
    ///     <para>
    ///         All or none: when one is not granted before the wait runs out, or the wait is cancelled, the ones already
    ///         taken are let go before the call throws. Disposing the handle lets go of every one.
    ///     </para>
    /// </remarks>
    public static Locking Lock(IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var sorted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A lock's name is blank.", nameof(names));
            }

            sorted.Add(name);
        }

        return new Locking([.. sorted], wait: null, cancellationToken);
    }
}
