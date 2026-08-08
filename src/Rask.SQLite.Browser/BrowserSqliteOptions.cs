namespace Rask.SQLite.Browser;

/// <summary>Configures a browser-hosted SQLite database and how often it is persisted to IndexedDB.</summary>
public sealed class BrowserSqliteOptions
{
    /// <summary>
    ///     The database's name — the file becomes <c>/rask/{Name}.db</c> and the snapshots live in an
    ///     IndexedDB store derived from it. Must not contain a path separator.
    /// </summary>
    public string Name { get; set; } = "app";

    /// <summary>
    ///     How often the owning tab writes the database back to IndexedDB. Defaults to 30 seconds.
    /// </summary>
    /// <remarks>
    ///     This is the real durability window, not the page-hide flush: the browser does not wait for a
    ///     <c>pagehide</c> handler, so a crashed or force-closed tab loses whatever changed since the last
    ///     interval. Shorten it if losing that much would matter; each tick copies the whole database
    ///     through SQLite's Online Backup API, so the cost scales with database size, not with churn.
    /// </remarks>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How many snapshots to keep. Defaults to 2 — the newest, plus one to fall back to if the newest
    ///     turns out to be unreadable. Each one costs its full database size in the origin's quota, so
    ///     this is not the place to keep history.
    /// </summary>
    public int Retain { get; set; } = 2;

    /// <summary>
    ///     Whether the owning tab asks the browser to exempt this origin's storage from eviction
    ///     (<c>navigator.storage.persist()</c>). Defaults to <see langword="true" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Worth asking, because the snapshots this package writes live in IndexedDB, and IndexedDB is
    ///         evictable: under storage pressure a browser may discard them, and the database would come
    ///         back empty on the next load with nothing to indicate why. A refusal costs nothing — the app
    ///         works exactly as before — so the default is to ask.
    ///     </para>
    ///     <para>
    ///         Chromium decides from engagement heuristics without prompting. <b>Firefox shows a permission
    ///         prompt</b>, and this is asked during startup rather than from a click, so an app that would
    ///         rather choose its moment should set this to <see langword="false" /> and call
    ///         <c>IStorageEstimator.RequestPersistAsync()</c> from a user-gesture handler instead.
    ///     </para>
    ///     <para>Only the owning tab asks: the others persist nothing, so a prompt there would buy nothing.</para>
    /// </remarks>
    public bool RequestPersistentStorage { get; set; } = true;

    /// <summary>
    ///     Where the database file lives, resolved from <see cref="Name" /> when the options are validated.
    /// </summary>
    /// <remarks>
    ///     Internal, and settable only so tests can point at a real temp directory: the production value is
    ///     an absolute path under <c>/rask</c>, which exists in the WASM runtime's in-memory filesystem and
    ///     nowhere else.
    /// </remarks>
    internal string DatabasePath { get; set; } = "";

    /// <summary>Throws <see cref="InvalidOperationException" /> if the options are out of range.</summary>
    internal void Validate()
    {
        // Name is validated by BrowserSqlite, which owns the rules about what a name may contain.
        if (DatabasePath.Length == 0)
        {
            DatabasePath = BrowserSqlite.DatabasePath(Name);
        }

        if (SnapshotInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(SnapshotInterval)} must be positive (was {SnapshotInterval}).");
        }

        if (Retain < 1)
        {
            throw new InvalidOperationException($"{nameof(Retain)} must be at least 1 (was {Retain}).");
        }
    }
}
