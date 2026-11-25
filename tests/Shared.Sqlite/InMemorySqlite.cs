using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Rask.Tests.Shared;

/// <summary>
///     A SQLite database in memory, of the test's own: a named, shared-cache database that one keeper connection
///     holds alive until this is disposed — so every connection the test opens to <see cref="ConnectionString" />
///     (a context, a factory, a raw <see cref="SqliteConnection" />) sees the same rows, and tests run side by side
///     with no file between them.
/// </summary>
/// <remarks>
///     <para>
///         Unpooled, as <c>TestDatabase.Start&lt;T&gt;()</c>'s own is: a pooled connection stays open after its
///         context closes it, and would keep the database alive past the test.
///     </para>
///     <para>
///         A test that is ABOUT a file — a backup, a restore, a database that survives a reopen — keeps a file of its
///         own; that is the only reason to.
///     </para>
/// </remarks>
public sealed class InMemorySqlite : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _keeper;

    /// <summary>Opens a database of its own, in memory, and holds it open.</summary>
    public InMemorySqlite()
    {
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = string.Create(CultureInfo.InvariantCulture, $"rask-test-{Guid.NewGuid():N}"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        }.ToString();
        _keeper = new SqliteConnection(ConnectionString);
        _keeper.Open();
    }

    /// <summary>The connection string every connection to this database opens with.</summary>
    public string ConnectionString { get; }

    /// <summary>Closes the keeper connection; once the last connection closes, the database is gone.</summary>
    public void Dispose() => _keeper.Dispose();

    /// <inheritdoc cref="Dispose" />
    public ValueTask DisposeAsync() => _keeper.DisposeAsync();
}
