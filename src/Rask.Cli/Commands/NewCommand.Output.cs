using System.Globalization;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Commands;

internal sealed partial class NewCommand
{
    private async Task<int> GenerateDirectAsync(
        TemplateInfo template, string name, string? output, bool dryRun, bool force, bool noRestore, bool noGit,
        bool asJson, ServerBatteries batteries, Func<string, string, ScaffoldResult> build, CancellationToken cancellationToken)
    {
        // rask new MyApp → ./MyApp/ ; --output overrides the destination directory.
        var targetDirectory = Scaffold.TargetDirectory(_workingDirectory, output, name);

        // build() is pure (in-memory strings), so it's safe to run before the existence check.
        var version = ResolvePackageVersion(CliMetadata.Version);
        var result = build(targetDirectory, version);

        // --dry-run previews the plan without touching disk or restoring.
        if (dryRun)
        {
            if (asJson)
            {
                JsonOutput.Write(Console, DryRunReport(template, name, targetDirectory, result), CliJsonContext.Default.NewDryRunReport);
            }
            else
            {
                WriteDryRunPlan(template, name, result);
            }

            return 0;
        }

        var restoreTarget = Path.Combine(targetDirectory, name + ".csproj");
        if (!force && await RefuseToOverwriteAsync(targetDirectory, restoreTarget, result).ConfigureAwait(false))
        {
            return 1;
        }

        WriteHeading($"Creating {template.DisplayName} '{name}'…");
        WriteScaffoldFiles(result);

        var (restoreFailed, buildFailed) = await RestoreAndBuildAsync(
            result, restoreTarget, targetDirectory, noRestore, cancellationToken).ConfigureAwait(false);

        // The database-backed batteries keep their state in tables that only exist once a migration has been
        // applied. The app applies its pending migrations itself when it starts, but it can only apply one that
        // exists — so the first migration is part of scaffolding rather than a step in the next-steps text.
        var migrated = !batteries.Data || noRestore || restoreFailed || buildFailed
            ? (bool?)null
            : await CreateFirstMigrationAsync(targetDirectory, PickEfProject(result, name), cancellationToken)
                .ConfigureAwait(false);

        // After the migration, so Migrations/ is in the initial commit rather than showing up as the first
        // uncommitted change in a project the user hasn't touched yet.
        await InitializeGitAsync(targetDirectory, noGit, cancellationToken).ConfigureAwait(false);

        return await ReportOutcomeAsync(result, targetDirectory, batteries, migrated, restoreFailed).ConfigureAwait(false);
    }

    private void WriteDryRunPlan(TemplateInfo template, string name, ScaffoldResult result)
    {
        WriteHeading($"Would create {template.DisplayName} '{name}':");
        foreach (var file in result.Files)
        {
            WriteDryRun("write", Path.GetRelativePath(_workingDirectory, file.Path));
        }
    }

    /// <summary>The same plan as <see cref="WriteDryRunPlan"/>, as the <c>--json</c> document.</summary>
    private NewDryRunReport DryRunReport(TemplateInfo template, string name, string targetDirectory, ScaffoldResult result) =>
        new(
            template.Key,
            name,
            Path.GetRelativePath(_workingDirectory, targetDirectory),
            [.. result.Files.Select(file => Path.GetRelativePath(_workingDirectory, file.Path))]);

    /// <summary>Reports, and answers true, when scaffolding here would overwrite something already on disk.</summary>
    private async Task<bool> RefuseToOverwriteAsync(string targetDirectory, string restoreTarget, ScaffoldResult result)
    {
        // The guard used to check only for the restore target, so scaffolding over a directory that already
        // held a Program.cs, a Features/ tree or a wwwroot silently overwrote them — with no --force to
        // consent to it and nothing to undo it. Any existing file is now enough to stop.
        if (_fileSystem.FileExists(restoreTarget))
        {
            var existing = Path.GetFileName(restoreTarget);
            Console.WriteErrorLine(
                $"A project already exists at '{targetDirectory}' ({existing}). Choose another name, --output, or pass --force.",
                ConsoleStyle.Error);
            return true;
        }

        var clashes = result.Files.Where(f => _fileSystem.FileExists(f.Path)).ToArray();
        if (clashes.Length == 0)
        {
            return false;
        }

        Console.WriteErrorLine($"'{targetDirectory}' already contains files this would overwrite:", ConsoleStyle.Error);
        foreach (var clash in clashes.Take(5))
        {
            await Console.Error.WriteLineAsync($"  {Path.GetRelativePath(_workingDirectory, clash.Path)}").ConfigureAwait(false);
        }

        if (clashes.Length > 5)
        {
            await Console.Error.WriteLineAsync($"  …and {(clashes.Length - 5).ToString(CultureInfo.InvariantCulture)} more").ConfigureAwait(false);
        }

        Console.WriteErrorLine("Choose another name or --output, or pass --force to overwrite.", ConsoleStyle.Error);
        return true;
    }

    private void WriteScaffoldFiles(ScaffoldResult result)
    {
        foreach (var file in result.Files)
        {
            var directory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            // Bytes for a file that is not text. Writing a PNG or an .ico through WriteAllText re-encodes it
            // as UTF-8: the scaffold succeeds, the build succeeds, and the image is quietly corrupt.
            if (file.Bytes is { } bytes)
            {
                _fileSystem.WriteAllBytes(file.Path, bytes);
            }
            else if (file.Secret)
            {
                _fileSystem.WriteSecretText(file.Path, file.Content);
            }
            else
            {
                _fileSystem.WriteAllText(file.Path, file.Content);
            }

            WriteCreated(Path.GetRelativePath(_workingDirectory, file.Path));
        }
    }

    /// <summary>
    /// Why <paramref name="output"/> can't be used, or null when it can.
    /// </summary>
    /// <remarks>
    /// Both failures used to surface late and badly. An empty (or whitespace) value resolved to the current
    /// directory, so the project was written <em>into wherever you were standing</em> instead of a folder of
    /// its own — no error, no clue. A value naming an existing file got as far as printing "Creating …"
    /// before the first write threw, and the resulting "The file '…' already exists." named the path without
    /// saying what the command had wanted with it.
    /// </remarks>
    private string? ValidateOutput(string? output)
    {
        if (output is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return "--output needs a directory. Leave it off to scaffold into ./<name>.";
        }

        var resolved = Path.GetFullPath(Path.Combine(_workingDirectory, output));
        return _fileSystem.FileExists(resolved)
            ? $"--output '{output}' is a file. Point it at a directory (it's created if missing)."
            : null;
    }
}
