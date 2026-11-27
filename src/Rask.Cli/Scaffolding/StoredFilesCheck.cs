using System.Formats.Tar;
using Microsoft.Data.Sqlite;

namespace Rask.Cli.Scaffolding;

/// <summary>
/// Rows without bytes: <c>StoredFile</c> rows on the disk provider whose file is not under the storage root — what a
/// database restored without its files looks like, and what would otherwise surface only as 404s.
/// </summary>
internal static class StoredFilesCheck
{
    private const int ExampleCount = 5;

    /// <summary>
    /// Check every disk row in <paramref name="databasePath"/> against <paramref name="root"/>. Null when the database
    /// has no stored-file table.
    /// </summary>
    /// <remarks>
    /// The table is found by its columns rather than by name: Rask names it <c>stored_files</c> (its columns <c>key</c>,
    /// <c>provider</c>, <c>sha256</c>), a database from before Rask's names has <c>StoredFile</c> (<c>Key</c>, …), and an app
    /// may name it as it likes. SQLite reads a column's name in any case, so one query reads either.
    /// </remarks>
    internal static MissingStoredFiles? FindMissing(string databasePath, string root)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();

        var table = FindTable(connection);
        if (table is null)
        {
            return null;
        }

        var boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var examples = new List<string>();
        int checkedRows = 0, missing = 0;

        using var command = connection.CreateCommand();
#pragma warning disable S2077 // an identifier cannot be a parameter; it is quoted, with its quotes doubled
        command.CommandText = $"SELECT \"Key\" FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\" WHERE \"Provider\" = 'Disk'";
#pragma warning restore S2077
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            checkedRows++;
            var key = reader.GetString(0);
            var path = Path.GetFullPath(Path.Combine(boundary, key));
            if (path.StartsWith(boundary, StringComparison.Ordinal) && File.Exists(path))
            {
                continue;
            }

            missing++;
            if (examples.Count < ExampleCount)
            {
                examples.Add(key);
            }
        }

        return new MissingStoredFiles(checkedRows, missing, examples);
    }

    private static string? FindTable(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT m.name FROM sqlite_master m
            WHERE m.type = 'table'
              AND EXISTS (SELECT 1 FROM pragma_table_info(m.name) WHERE name = 'key' COLLATE NOCASE)
              AND EXISTS (SELECT 1 FROM pragma_table_info(m.name) WHERE name = 'provider' COLLATE NOCASE)
              AND EXISTS (SELECT 1 FROM pragma_table_info(m.name) WHERE name = 'sha256' COLLATE NOCASE)
            ORDER BY m.name IN ('stored_files', 'StoredFile') DESC
            LIMIT 1
            """;
        return command.ExecuteScalar() as string;
    }
}
