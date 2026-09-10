using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     A scaffolded app can actually produce a migration, and apply it.
/// </summary>
/// <remarks>
///     <para>
///         Nothing covered this. <c>rask db add Init</c> is what <c>rask new</c> runs for you and what
///         the next-steps text falls back to, and the only assertions on it were that the
///         <em>sentence</em> appears — so a scaffold whose model EF cannot build would have printed the
///         instruction and failed the moment anyone followed it.
///     </para>
///     <para>
///         Driven through <see cref="DbCommand" /> rather than through <c>dotnet ef</c> directly, because
///         that is the path a user takes and it does work the raw tool does not: it adds
///         <c>Microsoft.EntityFrameworkCore.Design</c> to the startup project, which the EF tools refuse
///         to run without. Calling <c>dotnet ef</c> straight fails with a message about that package and
///         says nothing at all about the scaffold — which is how this test read as a broken app twice
///         while it was only ever testing the wrong thing.
///     </para>
/// </remarks>
public sealed class MigrationE2ETests
{
    [SkippableFact]
    public async Task A_scaffolded_app_can_add_and_apply_its_first_migration()
    {
        Skip.IfNot(CliBuildE2E.Enabled, CliBuildE2E.SkipReason);

        const string name = "E2EMigrate";
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;

        var temp = Path.Combine(Path.GetTempPath(), "rask-cli-e2e", Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(temp, name);
        try
        {
            var result = ProjectGenerator.GenerateServer(
                projectDir, name, new ServerBatteries { Data = true }, version);

            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, projectDir, feed);

            var project = Path.Combine(projectDir, name + ".csproj");

            // Restore first: everything below builds, and the packages come from the local feed.
            var (restoreExit, restoreOutput) = await CliBuildE2E.RunDotnet($"restore \"{project}\"");
            Assert.True(restoreExit == 0, $"restore failed.{CliBuildE2E.Diagnostics(restoreOutput)}");

            var console = new StringConsole();
            var db = new DbCommand(console, fs, new ProcessRunner(), projectDir, new Dictionary<string, string>(StringComparer.Ordinal));

            var add = await db.ExecuteAsync(["add", "Init", "--project", project], CancellationToken.None);
            Assert.True(add == 0, $"`rask db add Init` failed.\n{console.OutText}\n{console.ErrorText}");

            Assert.True(
                Directory.Exists(Path.Combine(projectDir, "Migrations")),
                $"the migration was reported as added but no Migrations directory exists.\n{console.OutText}");

            // Applying it is what proves the model EF built is one SQLite will actually accept — a model
            // that compiles can still be rejected when the DDL is emitted.
            var update = await db.ExecuteAsync(["update", "--project", project], CancellationToken.None);
            Assert.True(update == 0, $"`rask db update` failed.\n{console.OutText}\n{console.ErrorText}");

            Assert.True(
                File.Exists(Path.Combine(projectDir, "app.db")),
                $"the migration applied but no database was created.\n{console.OutText}");
        }
        finally
        {
            CliBuildE2E.TryDeleteDirectory(temp);
        }
    }
}
