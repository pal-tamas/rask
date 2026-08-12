using Microsoft.Data.Sqlite;

namespace Rask.SQLite.Browser;

/// <summary>
///     Where a browser app's SQLite database lives, and how to open it.
/// </summary>
/// <remarks>
///     The path is inside the WASM runtime's in-memory filesystem, not a real disk — nothing survives a
///     reload on its own. <see cref="BrowserSqliteServiceCollectionExtensions.AddRaskBrowserSqlite" /> is
///     what makes it durable, by restoring the file from IndexedDB at boot and writing it back.
/// </remarks>
public static class BrowserSqlite
{
    /// <summary>The directory browser databases live in.</summary>
    public const string DirectoryPath = "/rask";

    /// <summary>The database file path for <paramref name="name" />.</summary>
    public static string DatabasePath(string name) => $"{DirectoryPath}/{ValidateName(name)}.db";

    /// <summary>
    ///     A connection string for the browser database <paramref name="name" />, for
    ///     <c>UseSqlite(...)</c> or a raw <see cref="SqliteConnection" />.
    /// </summary>
    /// <remarks>
    ///     Pooling is off deliberately. Returning a connection to the pool calls <c>sqlite3_close_v2</c>'s
    ///     deactivation path, which un-registers EF Core's user-defined functions and produces
    ///     <c>SQLITE_BUSY</c> on close — root-caused in this repo already, and pooling buys nothing in a
    ///     single-threaded browser tab that opens one connection at a time anyway.
    /// </remarks>
    public static string ConnectionString(string name) =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath(name), Pooling = false }.ToString();

    /// <summary>The IndexedDB store that holds this database's snapshots.</summary>
    internal static string SnapshotStoreName(string name) => $"rask-sqlite-{ValidateName(name)}";

    /// <summary>The Web Lock that elects the one tab allowed to own this database.</summary>
    internal static string OwnerLockName(string name) => $"rask-sqlite-owner:{ValidateName(name)}";

    // The name becomes a file name, an IndexedDB database name and a lock name, so a stray slash or an
    // empty string would fail somewhere far from the call that caused it.
    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.AsSpan().IndexOfAny('/', '\\') >= 0)
        {
            throw new ArgumentException(
                $"Database name '{name}' must not contain a path separator — it names a file in {DirectoryPath}, not a path.",
                nameof(name));
        }

        return name;
    }
}
