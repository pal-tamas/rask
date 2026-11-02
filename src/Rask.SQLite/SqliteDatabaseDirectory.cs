using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
/// Creates the directory a server database file lives in, so <c>Data Source=/tmp/rask/app.db</c> opens on a fresh
/// container instead of failing the first start with <c>SQLite Error 14: 'unable to open database file'</c>.
/// SQLite creates the file, never the directories above it.
/// </summary>
internal static class SqliteDatabaseDirectory
{
    /// <summary>
    /// Creates the parent directory of <paramref name="connectionString"/>'s database file when it is missing.
    /// An in-memory database, a read-only open, a <c>file:</c> URI and a <c>|DataDirectory|</c> path are left
    /// alone: none of them names a directory this should make.
    /// </summary>
    /// <returns>The directory that was created, or <see langword="null"/> when nothing was.</returns>
    internal static string? EnsureFor(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource)
            || builder.Mode is SqliteOpenMode.Memory or SqliteOpenMode.ReadOnly
            || string.Equals(dataSource, ":memory:", StringComparison.Ordinal)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            || dataSource.Contains('|', StringComparison.Ordinal))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        return directory;
    }
}
