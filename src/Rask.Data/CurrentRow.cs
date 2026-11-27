using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Rask.Data;

/// <summary>
///     One of the app's own rows the work in flight is for — its tenant or its user — and the cache it is read from,
///     kept per database and keyed there by the <see cref="Guid" /> the framework carries that key in.
/// </summary>
/// <remarks>
///     <para>
///         Filled as work starts (<see cref="Prime" />), so the synchronous read <c>Current.Tenant</c> is a
///         dictionary lookup. A miss — a key nothing primed, a row a save has just dropped — is answered by ONE
///         synchronous query on the spot (<see cref="Read" />), counted, and kept.
///     </para>
///     <para>
///         The floor every answer stands on: a row is only ever looked up by the key in flight NOW, so a nested
///         <c>Tenant.Use</c> never sees the outer tenant's row; a key with no row throws rather than reading as
///         nobody, and nothing is cached for it; a row whose own key is not the one asked for throws and is not
///         kept. A save of the aggregate drops everything held (<see cref="Forget" />), and an entry lives
///         <see cref="Lifetime" /> at most, for the saves another node made.
///     </para>
///     <para>
///         A key names a row in ONE database, so the rows are held per database (<see cref="ReadDb.IdentityFor" />):
///         two apps in one process — two tests side by side, each on its own <c>TestDatabase</c>, both with a tenant
///         2 — never read each other's row. The databases are held weakly, so one let go of takes its rows with it.
///     </para>
/// </remarks>
internal abstract class CurrentRow(CurrentRowKind kind, Type aggregate)
{
    /// <summary>How long a row is trusted for: the bound on a save another process made going unseen.</summary>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    // Past this many entries, an insert first drops the ones whose time is up: a process that has served many users
    // keeps the ones it is serving, not every one it ever saw.
    private const int SweepAbove = 4096;

    private readonly ConditionalWeakTable<object, ConcurrentDictionary<Guid, Held>> _held = new();
    private long _generation;

    /// <summary>Which row this is.</summary>
    internal CurrentRowKind Kind => kind;

    /// <summary>The aggregate whose save drops what is held.</summary>
    internal Type Aggregate => aggregate;

    /// <summary>Whether the aggregate is keyed by a number, carried inside the <see cref="Guid" /> (<see cref="TenantNumber" />).</summary>
    internal virtual bool IsNumbered => false;

    /// <summary>The row keyed <paramref name="key" />: held, or read by a synchronous query and kept.</summary>
    /// <exception cref="MissingTenantException">The tenant has no row: it was deleted, or never existed.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The user has no row, or the row the query found is keyed by another key than the one asked for.
    /// </exception>
    internal object Read(Guid key)
    {
        var database = ReadDb.IdentityFor(null);
        if (TryHeld(database, key, out var held))
        {
            return held;
        }

        var generation = Volatile.Read(ref _generation);
        CurrentRows.CountQuery(kind);

        return Keep(database, key, Load(key), generation)
               ?? throw Missing(key);
    }

    /// <summary>
    ///     Whether a row keyed <paramref name="key" /> is held, and still trusted, for the database the work of
    ///     <paramref name="scope" /> reads — or the ambient work's, when null.
    /// </summary>
    internal bool Holds(IServiceProvider? scope, Guid key) => TryHeld(ReadDb.IdentityFor(scope), key, out _);

    /// <summary>Reads the row keyed <paramref name="key" /> ahead of the work that may ask for it, and keeps it.</summary>
    /// <remarks>
    ///     Says nothing of a key with no row: the read that asks for it is the one that is told. A failure is the
    ///     caller's to decide about.
    /// </remarks>
    internal async ValueTask Prime(Guid key, CancellationToken cancellationToken) =>
        _ = await Exists(key, cancellationToken).ConfigureAwait(false);

    /// <summary>
    ///     Whether a row keyed <paramref name="key" /> exists: held, or read now and kept. False for a key that cannot be
    ///     one of this aggregate's — a <see cref="Guid" /> carrying no number, for a numbered aggregate.
    /// </summary>
    /// <remarks>A failure to read is the caller's to decide about, as <see cref="Prime" />'s is.</remarks>
    internal async ValueTask<bool> Exists(Guid key, CancellationToken cancellationToken)
    {
        var database = ReadDb.IdentityFor(null);
        if (TryHeld(database, key, out _))
        {
            return true;
        }

        if (!Names(key))
        {
            return false;
        }

        var generation = Volatile.Read(ref _generation);
        var loaded = await LoadAsync(key, cancellationToken).ConfigureAwait(false);
        if (loaded is null || !Owns(loaded, key))
        {
            return false;
        }

        Store(database, key, loaded, generation);
        return true;
    }

    /// <summary>Drops every row held: one of them was just saved.</summary>
    /// <remarks>
    ///     <para>
    ///         The generation moves first, so a read that started before the save and lands after it is not kept
    ///         either: it may hold the row as it was — and an entry is trusted only under the generation it was kept
    ///         in, so one a racing read stored after the clear is not served.
    ///     </para>
    ///     <para>
    ///         Every database's rows go, not only the saving one's: which read database a write context saves to is
    ///         not knowable here, and a save of the tenant or the user is rare enough that the next read's one
    ///         query is the cheaper answer.
    ///     </para>
    /// </remarks>
    internal void Forget()
    {
        Interlocked.Increment(ref _generation);
        foreach (var (_, held) in _held)
        {
            held.Clear();
        }
    }

    /// <summary>The row keyed <paramref name="key" />, read now by a synchronous query, or null when there is none.</summary>
    protected abstract object? Load(Guid key);

    /// <summary>The row keyed <paramref name="key" />, or null when there is none.</summary>
    protected abstract Task<object?> LoadAsync(Guid key, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="row" />'s own key is <paramref name="key" />.</summary>
    protected abstract bool Owns(object row, Guid key);

    /// <summary>Whether <paramref name="key" /> can name a row of this aggregate at all, read as the key it is keyed by.</summary>
    protected virtual bool Names(Guid key) => true;

    private bool TryHeld(object database, Guid key, out object row)
    {
        if (_held.TryGetValue(database, out var rows) &&
            rows.TryGetValue(key, out var held) &&
            held.Generation == Volatile.Read(ref _generation) &&
            Environment.TickCount64 < held.Until)
        {
            row = held.Row;
            return true;
        }

        row = null!;
        return false;
    }

    private object? Keep(object database, Guid key, object? loaded, long generation)
    {
        if (loaded is null)
        {
            return null;
        }

        if (!Owns(loaded, key))
        {
            throw new InvalidOperationException(
                $"The {Name} row read for {key} carries another key, so it is not this work's {Name} and is not " +
                "kept. The key column and the key the work is for disagree — a value converter on the key, or a " +
                "text key in another form than the Guid it carries.");
        }

        Store(database, key, loaded, generation);
        return loaded;
    }

    private void Store(object database, Guid key, object row, long generation)
    {
        // Read inside a Db.Transaction, the row may be one that transaction wrote and has not committed — or will
        // roll back. It answers this work, and is not kept for anybody else's.
        if (AmbientTransaction.Current is not null || generation != Volatile.Read(ref _generation))
        {
            return;
        }

        var rows = _held.GetValue(database, static _ => new ConcurrentDictionary<Guid, Held>());
        if (rows.Count > SweepAbove)
        {
            var now = Environment.TickCount64;
            foreach (var entry in rows)
            {
                if (entry.Value.Until <= now)
                {
                    rows.TryRemove(entry);
                }
            }
        }

        rows[key] = new Held(row, Environment.TickCount64 + (long)Lifetime.TotalMilliseconds, generation);
    }

    private string Name => kind is CurrentRowKind.Tenant ? "tenant" : "user";

    private Exception Missing(Guid key) =>
        kind is CurrentRowKind.Tenant
            ? new MissingTenantException(
                $"The work in flight belongs to the tenant {key}, and its {aggregate.Name} row is gone — deleted, " +
                "or never written — so there is no Current.Tenant to read. A tenant whose row is deleted has no data " +
                "to work in: end its users' sign-ins, or stop resolving it.")
            : new InvalidOperationException(
                $"The work in flight is for the user {key}, and their {aggregate.Name} row is gone — deleted, or " +
                "never written — so there is no Current.User to read. A deleted user's sign-in should end with them: " +
                "read Current.UserId where a missing row is an answer you handle.");

    private sealed record Held(object Row, long Until, long Generation);
}
