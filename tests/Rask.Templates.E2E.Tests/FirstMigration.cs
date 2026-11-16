using Microsoft.Data.Sqlite;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     A scaffolded app's first migration, made the way <c>rask new</c> makes it, and read back out of the database the
///     app applied it to.
/// </summary>
/// <remarks>
///     <para>
///         The gate writes a scaffold with <see cref="ProjectGenerator" /> rather than running <c>rask new</c>, so the step
///         <c>rask new</c> takes after its restore — <c>rask db add Init</c> — is not taken for it. Without it a data app
///         starts over an empty database: the batteries log "no such table" and no account can register (#1334).
///     </para>
///     <para>
///         Through <see cref="DbCommand" />, the code <c>rask new</c> itself delegates to, with the client publish off as it
///         turns it off, so the project ends in the state a user's does.
///     </para>
/// </remarks>
internal static class FirstMigration
{
    /// <summary>The name <c>rask new</c> gives the first migration.</summary>
    public const string Name = "Init";

    /// <summary>Adds the first migration to <paramref name="projectFile" />; the caller builds the project after it.</summary>
    public static async Task CreateAsync(string projectFile)
    {
        var console = new StringConsole();
        var db = new DbCommand(console, new SystemFileSystem(), new ProcessRunner(), Path.GetDirectoryName(projectFile)!,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["RaskSpaBuild"] = "false" });

        var exit = await db.ExecuteAsync(["add", Name, "--project", projectFile], TestContext.Current.CancellationToken);

        Assert.True(exit == 0, $"the first migration failed.\n{console.OutText}\n{console.ErrorText}");
    }

    /// <summary>The migrations EF Core recorded as applied to the SQLite database at <paramref name="databaseFile" />.</summary>
    public static IReadOnlyList<string> Applied(string databaseFile)
    {
        Assert.True(File.Exists(databaseFile), $"the app made no database at {databaseFile}");

        using var connection = new SqliteConnection($"Data Source={databaseFile};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId";
        using var reader = command.ExecuteReader();

        var applied = new List<string>();
        while (reader.Read())
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }
}
