using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

internal sealed partial class NewCommand
{
    private async Task<(bool RestoreFailed, bool BuildFailed)> RestoreAndBuildAsync(
        ScaffoldResult result, string restoreTarget, string targetDirectory, bool noRestore, CancellationToken cancellationToken)
    {
        // Package refs are already baked into the csproj(s) at the pinned version; restore pulls them so the
        // project builds immediately. The files on disk are complete and correct either way — but a failed
        // restore leaves a project that won't build, so it is reported as a failure rather than a warning
        // that `rask new && dotnet build` would step straight past. --no-restore skips it deliberately.
        if (noRestore)
        {
            Console.WriteLine("Skipped restore (--no-restore) — run 'dotnet restore' before building.", ConsoleStyle.Dim);
            return (false, false);
        }

        await ReportUnpublishedPackagesAsync(result, cancellationToken).ConfigureAwait(false);
        Console.WriteLine("Restoring packages…", ConsoleStyle.Dim);
        if (await _process.RunAsync("dotnet", ["restore", restoreTarget], targetDirectory, cancellationToken).ConfigureAwait(false) != 0)
        {
            return (true, false);
        }

        // Built here, before anything else touches it, so "does this compile?" is answered by the compiler
        // rather than inferred from whatever fails next. The step that used to be first — creating the
        // migration — builds the project as a side effect of loading the DbContext, so a scaffold that did
        // not compile surfaced as an EF failure under a line reading "Creating the first migration…",
        // which names neither the file nor the error. A build says it plainly and stops.
        Console.WriteLine("Building…", ConsoleStyle.Dim);

        // The same front-end skip the migration step uses: with the batteries on, a plain build runs
        // the bundler (or, on the meta lane, a full production front-end build) — minutes of silence
        // for output nobody reads before `rask dev` turns both off again.
        var buildFailed = await _process.RunAsync(
            "dotnet",
            ["build", restoreTarget, .. SkipFrontEndBuild.Select(p => $"-p:{p.Key}={p.Value}")],
            targetDirectory,
            cancellationToken).ConfigureAwait(false) != 0;

        return (false, buildFailed);
    }

    private async Task<int> ReportOutcomeAsync(
        ScaffoldResult result, string targetDirectory, ServerBatteries batteries, bool? migrated, bool restoreFailed)
    {
        if (!string.IsNullOrEmpty(result.Notes))
        {
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
            await Console.Out.WriteLineAsync(result.Notes).ConfigureAwait(false);
        }

        // Only now that it has happened. Said by the generator ahead of time, it appeared under a restore that had
        // failed and a migration that never ran (#1083).
        if (migrated == true)
        {
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
            await Console.Out.WriteLineAsync("The first migration is in Migrations/; the app applies it to app.db when it starts.").ConfigureAwait(false);
        }

        if (restoreFailed)
        {
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
            Console.WriteErrorLine(
                $"The project was written to '{targetDirectory}', but restoring its packages failed — it won't build until that succeeds.",
                ConsoleStyle.Error);
            Console.WriteErrorLine("Run 'dotnet restore' there once you're online, or re-run with --no-restore to skip this step.", ConsoleStyle.Error);
            return 1;
        }

        // A warning rather than a failure, and deliberately: the files on disk are complete and correct,
        // and this step is the only one that can need a network for something other than packages (the
        // dotnet-ef tool install). Failing the command would make an offline `rask new` look like it
        // scaffolded nothing.
        if (batteries.Data && migrated != true)
        {
            await Console.Out.WriteLineAsync().ConfigureAwait(false);
            WriteFirstMigrationInstructions(migrated is null);
        }

        return 0;
    }

    /// <summary>
    /// The project that owns the <c>DbContext</c>: the <c>.Server</c> half of a multi-project template,
    /// the single project otherwise.
    /// </summary>
    /// <remarks>
    /// Read off the scaffold rather than rebuilt from the template key. <c>ProjectLocator</c> can't be used
    /// here: it walks <em>up</em> from the working directory, so on a template whose root holds only a
    /// <c>.slnx</c> it would climb out of the new project and migrate whatever it found above it.
    /// </remarks>
    private static string? PickEfProject(ScaffoldResult result, string name)
    {
        var projects = result.Files
            .Select(file => file.Path)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return projects.FirstOrDefault(path =>
                   Path.GetFileName(path).Equals(name + ".Server.csproj", StringComparison.OrdinalIgnoreCase))
               ?? projects.FirstOrDefault(path =>
                   Path.GetFileName(path).Equals(name + ".csproj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Create the project's first migration, which the app applies itself on its first start.
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="DbCommand"/> rather than reimplemented against <c>dotnet ef</c>: it already
    /// installs the EF tools on first use, adds the design package the tools require, and builds the
    /// argument list. Running the same code the user would run next means the project ends up in exactly
    /// the state <c>rask db add Init</c> leaves it in. Not applied here: <c>RaskApp</c> migrates on start.
    ///
    /// <para>
    /// <c>--project</c> is passed explicitly for the reason given on <see cref="PickEfProject"/>.
    /// </para>
    /// </remarks>
    private async Task<bool> CreateFirstMigrationAsync(
        string targetDirectory, string? efProject, CancellationToken cancellationToken)
    {
        if (efProject is null)
        {
            return false;
        }

        // Skip the front-end build for this one build. `dotnet-ef` builds the project to load the
        // DbContext, and with the batteries on that build defaults to RaskSpaBuild/RaskMetaBuild=true —
        // so scaffolding a front-end template ran the bundler, or on the meta lane a full Nuxt/Next
        // production build, behind a line that says "Creating the first migration…". Minutes of silence
        // for output nobody reads: the next thing anyone does is `rask dev`, which turns both off again
        // and lets the framework's own dev server own the front end. A missing node then failed the
        // migration step for a reason that has nothing to do with the database.
        var db = new DbCommand(Console, _fileSystem, _process, targetDirectory, SkipFrontEndBuild);

        Console.WriteLine("Creating the first migration…", ConsoleStyle.Dim);
        return await db.ExecuteAsync(["add", "Init", "--project", efProject], cancellationToken).ConfigureAwait(false) == 0;
    }

    /// <summary>What to run when the first migration was skipped or didn't succeed.</summary>
    private void WriteFirstMigrationInstructions(bool skipped)
    {
        Console.WriteLine(
            skipped
                ? "The first migration was skipped along with the restore. Before the first run:"
                : "The first migration didn't complete. Before the first run:",
            ConsoleStyle.Dim);
        Console.Out.WriteLine("  rask db add Init");
        Console.WriteLine(
            "The app applies its migrations when it starts, but it needs one to apply: the batteries keep "
            + "their state in tables only a migration creates.",
            ConsoleStyle.Dim);
    }

    /// <summary>
    /// Put the new project under version control: <c>git init</c>, stage everything, and make one commit, so
    /// the very first thing the user changes is already a diff against a known-good starting point.
    /// <para>
    /// Every part of this is best-effort. Git may not be installed, may have no <c>user.email</c> configured,
    /// or the directory may already be inside a repository — none of which is a reason to fail a scaffold that
    /// otherwise succeeded, so a failure downgrades to a dim note. <c>--no-git</c> skips it outright.
    /// </para>
    /// </summary>
    private async Task InitializeGitAsync(string targetDirectory, bool noGit, CancellationToken cancellationToken)
    {
        if (noGit)
        {
            return;
        }

        // Already inside a repository (a monorepo, or `rask new` into an existing checkout): the files belong
        // to that history. Initialising a nested repo there would quietly detach them from it.
        if (await IsInsideRepositoryAsync(targetDirectory, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The commit identity is supplied per-command rather than written to the repo's config: a machine with
        // no global user.email would otherwise fail the commit, and one with an identity keeps using its own
        // from the second commit onwards.
        var steps = new[]
        {
            new[] { "init", "--quiet" },
            ["add", "--all"],
            ["-c", "user.name=rask", "-c", "user.email=rask@localhost", "commit", "--quiet", "-m", "Initial commit from rask new"],
        };

        foreach (var step in steps)
        {
            if (await _process.RunAsync("git", step, targetDirectory, cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteLine("Skipped git setup — initialize the repository yourself with 'git init'.", ConsoleStyle.Dim);
                return;
            }
        }

        Console.WriteLine("Initialized a git repository with one commit.", ConsoleStyle.Dim);
    }

    // Names the packages the restore is about to fail on, and why, before its NU1103 output buries it (#1083). The restore
    // still runs: the feed may be one the machine does not use, and the restore's own answer is the authoritative one.
    private async Task ReportUnpublishedPackagesAsync(ScaffoldResult result, CancellationToken cancellationToken)
    {
        if (Feed is null)
        {
            return;
        }

        var unpublished = await Feed
            .FindUnpublishedAsync(PackageFeed.RaskReferences(result.Files), cancellationToken)
            .ConfigureAwait(false);
        if (unpublished.Count == 0)
        {
            return;
        }

        Console.WriteErrorLine("These packages have no published version on nuget.org at the version this project pins:", ConsoleStyle.Error);
        foreach (var (id, version) in unpublished.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            Console.WriteErrorLine($"  {id} {version}", ConsoleStyle.Error);
        }

        Console.WriteErrorLine(
            "The restore below will fail with NU1103 until they are released at that version. A package added since the "
            + "last release is the usual reason; pin a version that exists in the .csproj, or turn the battery off.",
            ConsoleStyle.Error);
    }

    /// <summary>True when <paramref name="directory"/> already sits inside a git working tree.</summary>
    private async Task<bool> IsInsideRepositoryAsync(string directory, CancellationToken cancellationToken)
    {
        var result = await _process
            .CaptureAsync("git", ["rev-parse", "--is-inside-work-tree"], directory, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0 && result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
