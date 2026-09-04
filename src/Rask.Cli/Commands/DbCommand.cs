using System.Text.Json;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask db</c> — the EF Core migration lifecycle behind friendly verbs. Each subcommand wraps a
/// <c>dotnet ef</c> command (<c>add</c>/<c>remove</c>/<c>list</c> → <c>migrations …</c>,
/// <c>update</c>/<c>drop</c> → <c>database …</c>), auto-resolving the target project from the current
/// directory and installing the <c>dotnet-ef</c> tool on first use if it's missing. Anything after
/// <c>--</c> is forwarded to <c>dotnet ef</c> verbatim (an escape hatch for <c>--verbose</c> etc.).
/// </summary>
/// <param name="buildEnvironment">
///     Overlaid onto the environment of the <c>dotnet-ef</c> child process, which is how MSBuild
///     properties reach a build this command does not own the command line of. <c>rask new</c> passes
///     <c>RaskSpaBuild=false</c> / <c>RaskMetaBuild=false</c> so scaffolding's first migration does not
///     run a production front-end build. Null — no overlay — everywhere else.
/// </param>
internal sealed partial class DbCommand(
    IConsole console,
    IFileSystem fileSystem,
    IProcessRunner process,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? buildEnvironment = null)
    : CliCommand(console)
{
    private readonly IReadOnlyDictionary<string, string>? _buildEnvironment = buildEnvironment;

    // These two never touch EF: they copy a file through SQLite (locally) or through a container on
    // the host, so they must not pay for a dotnet-ef install, and must work on an app that has none.
    private static readonly string[] FileSubcommands = ["backup", "restore"];

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "db";

    public override string Summary => "Manage EF Core migrations, and back the database up or restore it.";

    // The actions and options are listed in full below; spelling them out here too only gave them a
    // second place to drift from.
    public override string Usage => "rask db <action> [<name|file>] [options] [-- <ef args>]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
    [
        ("[<name|file>]", "Migration name for 'add' (e.g. InitialCreate), or the backup file for 'restore'."),
    ];

    public override IReadOnlyList<string> Examples =>
    [
        "rask db add InitialCreate",
        "rask db update",
        "rask db list",
        "rask db drop --yes",
        "rask db backup",
        "rask db backup --remote --output backups/",
        "rask db restore backups/shop-20260805-081500.db --remote",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Verb("add", "Create a migration from the current model.")
            .Verb("remove", "Delete the most recent migration.")
            .Verb("list", "List the migrations and which are applied.")
            .Verb("update", "Apply pending migrations to the database.")
            .Verb("drop", "Delete the database and everything in it.")
            .Verb("backup", "Copy the database to a file.")
            .Verb("restore", "Replace the database from a backup file.")
            .Option("project", 'p', "path", "Project containing the DbContext (default: found from the current directory).")
            .Option("startup-project", 's', "path", "Startup project (default: same as --project).")
            .Option("context", 'c', "Name", "DbContext to use when the project defines more than one.")
            .Option("output", 'o', "path", "Migration output directory (add), or the backup file or directory to write (backup).")
            .Flag("remote", null, "Act on the deployed database instead of the local one (backup, restore).")
            .Option("host", null, "user@host", "Deployment host for --remote (default: the one in .rask/deploy.json).")
            .Option("app", null, "name", "Deployed app name for --remote (default: the one in .rask/deploy.json).")
            // Renamed from --force. On new/generate that word means "overwrite files"; here it meant
            // "don't ask me" — one spelling for two unrelated powers, and the one that destroys a
            // database was the one you could reach by muscle memory from the one that overwrites a file.
            // --yes/-y is what every other tool calls this (#601).
            .Flag("yes", 'y', "Skip the confirmation prompt (drop, restore).")
            .Flag("dry-run", description: "Print what would run without touching the database.")
            .WithJson();

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = CreateSchema();
        if (!schema.TryResolveVerb(args.FirstOrDefault(), out var subcommand))
        {
            return FailUnknownVerb(args.FirstOrDefault(), schema);
        }

        var parsed = schema.Parse(args.Skip(1).ToArray());
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        if (parsed.Positionals.Count > 1)
        {
            return Fail($"Unexpected argument '{parsed.Positionals[1]}'.");
        }

        var name = parsed.Positionals.FirstOrDefault();
        if (!ValidatePositional(subcommand, name, out var positionalError))
        {
            return Fail(positionalError!);
        }

        var output = parsed.Option("output");
        if (output is not null && subcommand is not ("add" or "backup"))
        {
            return Fail("--output only applies to 'rask db add' and 'rask db backup'.");
        }

        if (parsed.HasFlag("yes") && subcommand is not ("drop" or "restore"))
        {
            return Fail("--yes only applies to 'rask db drop' and 'rask db restore'.");
        }

        var remote = parsed.HasFlag("remote");
        foreach (var option in new[] { "remote", "host", "app" })
        {
            var supplied = option == "remote" ? remote : parsed.Option(option) is not null;
            if (supplied && !FileSubcommands.Contains(subcommand))
            {
                return Fail($"--{option} only applies to 'rask db backup' and 'rask db restore'.");
            }
        }

        if (!remote && (parsed.Option("host") is not null || parsed.Option("app") is not null))
        {
            return Fail("--host and --app only apply with --remote.");
        }

        // An explicit --project wins; otherwise fall back to the single .csproj at or above the CWD. EF
        // accepts a directory here and finds the project inside it, so the located directory is enough.
        //
        // A remote backup/restore is the one case that needs no project at all: it acts on a container on
        // another machine, identified by host and app name. Requiring a .csproj would stop you taking a
        // copy of production from a scratch directory, or from CI.
        var project = parsed.Option("project");
        if (project is null)
        {
            var located = ProjectLocator.Locate(_fileSystem, _workingDirectory);
            if (located is null && !(remote && FileSubcommands.Contains(subcommand)))
            {
                Console.WriteErrorLine(
                    $"{ProjectLocator.DescribeMissing(_fileSystem, _workingDirectory)} Run this inside a project, or pass --project.",
                    ConsoleStyle.Error);
                return 1;
            }

            project = located?.ProjectDirectory ?? _workingDirectory;
        }

        // The startup project configures the DbContext (DI); in a typical single-project Rask app it's the
        // same project that owns the migrations, so it defaults to --project.
        var startupProject = parsed.Option("startup-project") ?? project;

        // backup/restore branch off before the EF tooling: they copy a database, they don't migrate one, so
        // they must not install dotnet-ef or require the project to reference EF's design package.
        if (FileSubcommands.Contains(subcommand))
        {
            // Both of these copy a database *file* — locally through SQLite's Online Backup API, remotely
            // with VACUUM INTO in a sidecar.
            //
            // Restore replaces a database and, when remote, stops the app to do it — so "what exactly
            // would this touch" is worth being able to ask without finding out (#600).
            if (parsed.HasFlag("dry-run"))
            {
                var where = remote
                    ? $"the deployed database on {parsed.Option("host") ?? "the remembered host"}"
                    : $"the local database of {project}";
                WriteDryRun(subcommand, where);
                if (output is not null)
                {
                    WriteDryRun("write to", output);
                }

                if (name is not null)
                {
                    WriteDryRun("read from", name);
                }

                return 0;
            }

            return await ExecuteFileActionAsync(
                subcommand,
                name,
                project,
                output,
                remote,
                parsed.Option("host"),
                parsed.Option("app"),
                parsed.HasFlag("yes"),
                cancellationToken).ConfigureAwait(false);
        }

        // Before the confirmation and before the dotnet-ef install: a dry run changes nothing, so asking
        // permission for it — or refusing it outright for want of a terminal, which is what happened —
        // makes the one safe way to inspect a destructive command the hardest to reach. `drop` and
        // `update` are the destructive and the slow one, and neither told you what it was about to do
        // against which project (#600).
        if (parsed.HasFlag("dry-run"))
        {
            WriteDryRun(
                "run",
                "dotnet " + string.Join(
                    ' ',
                    BuildEfArguments(
                        subcommand, name, project, startupProject, parsed.Option("context"), output,
                        parsed.HasFlag("yes"), parsed.Passthrough)));
            WriteDryRun("run it in", _workingDirectory);
            return 0;
        }

        // `--yes`'s own help has always said it "skips the confirmation prompt" — and there was no
        // prompt. `dotnet ef database drop` does its own, but only when it has a terminal, so a drop run
        // from a script destroyed the database with nothing asked. Ask here, where we know the answer
        // matters, and refuse rather than guess when there's nobody to ask.
        if (subcommand == "drop" && !parsed.HasFlag("yes"))
        {
            if (Console.IsInputRedirected)
            {
                Console.WriteErrorLine("`rask db drop` deletes the database. Pass --yes to confirm — there's no terminal to ask on.", ConsoleStyle.Error);
                return 1;
            }

            if (!new Prompt(Console).Confirm($"Drop the database for '{Path.GetFileName(startupProject)}'? This deletes it and everything in it.", @default: false))
            {
                Console.Out.WriteLine("Left it alone.");
                return 0;
            }
        }

        if (!await EfToolProbe.EnsureAsync(_process, Console, cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        await EnsureDesignPackageAsync(startupProject, cancellationToken).ConfigureAwait(false);

        var efArgs = BuildEfArguments(subcommand, name, project, startupProject, parsed.Option("context"), output, parsed.HasFlag("yes"), parsed.Passthrough);

        if (parsed.HasFlag("json"))
        {
            return await ListAsJsonAsync(efArgs, cancellationToken).ConfigureAwait(false);
        }

        return await _process.RunAsync("dotnet", efArgs, _workingDirectory, cancellationToken, _buildEnvironment)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     <c>rask db list --json</c>. Asks <c>dotnet ef</c> for JSON rather than parsing its human
    ///     listing, then re-emits it in this CLI's shape.
    /// </summary>
    /// <remarks>
    ///     EF prints a preamble to stdout before the document — "Build started...", "Build succeeded.",
    ///     and sometimes a tools-version warning — so the payload has to be found rather than assumed to
    ///     start at byte zero. It is not delimited by markers; the array simply begins at the first line
    ///     that is <c>[</c>. Verified against real <c>dotnet ef migrations list --json</c> output, and
    ///     pinned by a fixture test using a captured copy of it.
    /// </remarks>
    private async Task<int> ListAsJsonAsync(IReadOnlyList<string> efArgs, CancellationToken cancellationToken)
    {
        var result = await _process
            .CaptureAsync("dotnet", [.. efArgs, "--json"], _workingDirectory, cancellationToken, _buildEnvironment)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            // EF already explained itself on stderr; repeating it here in a different voice would only
            // make the real message harder to find.
            Console.WriteErrorLine(result.StandardError.Trim(), ConsoleStyle.Error);
            return 1;
        }

        if (ExtractJsonArray(result.StandardOutput) is not { } payload)
        {
            return Fail(
                "Could not find the migration list in `dotnet ef`'s output. Run `rask db list` without "
                + "--json to see what it printed.");
        }

        var migrations = JsonSerializer.Deserialize(payload, CliJsonContext.Default.EfMigrationArray) ?? [];
        JsonOutput.Write(
            Console,
            new MigrationListReport([.. migrations.Select(m => new MigrationEntry(m.Id, m.Name, m.Applied))]),
            CliJsonContext.Default.MigrationListReport);
        return 0;
    }

    /// <summary>The JSON document inside EF's output, or null when there isn't one.</summary>
    internal static string? ExtractJsonArray(string stdout)
    {
        var lines = stdout.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "[")
            {
                return string.Join('\n', lines.Skip(i));
            }
        }

        return null;
    }

    // The EF Core tools need the startup project to reference Microsoft.EntityFrameworkCore.Design;
    // without it `dotnet ef` fails with a terse message. Projects scaffolded with `--data` already
    // include it, but a hand-built one (or a demo that uses EnsureCreated) may not — so add it for the
    // user (like the dotnet-ef tool install
    // above). `dotnet add package` restores too, so the subsequent `dotnet ef` build picks it up. This
    // never blocks: if we can't confidently read the startup project's csproj we stay quiet rather than
    // touch a setup we can't see (e.g. the ref comes from imported props), and a failed add still lets
    // `dotnet ef` run so the user sees EF's own guidance.
    private async Task EnsureDesignPackageAsync(string startupProject, CancellationToken cancellationToken)
    {
        string csproj;
        try
        {
            var resolved = ResolveCsproj(startupProject);
            if (resolved is null || _fileSystem.ReadAllText(resolved).Contains("Microsoft.EntityFrameworkCore.Design", StringComparison.Ordinal))
            {
                return;
            }

            csproj = resolved;
        }
        catch (IOException)
        {
            // Unreadable project file — leave it to `dotnet ef` rather than fail the command over it.
            return;
        }

        Console.WriteLine("Adding Microsoft.EntityFrameworkCore.Design to the startup project (required by the EF Core tools)…", ConsoleStyle.Dim);
        var exit = await _process.RunAsync("dotnet", ["add", csproj, "package", "Microsoft.EntityFrameworkCore.Design"], _workingDirectory, cancellationToken).ConfigureAwait(false);
        if (exit != 0)
        {
            WriteWarning($"  Couldn't add it automatically — add it manually: dotnet add \"{csproj}\" package Microsoft.EntityFrameworkCore.Design");
        }
    }

    // Resolve a --project / --startup-project value (a .csproj path or a directory) to its csproj file,
    // or null when it can't be pinned to exactly one — mirroring how ProjectLocator treats ambiguity.
    private string? ResolveCsproj(string projectPathOrDirectory)
    {
        if (projectPathOrDirectory.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return _fileSystem.FileExists(projectPathOrDirectory) ? projectPathOrDirectory : null;
        }

        var projects = _fileSystem.ListFiles(projectPathOrDirectory, "*.csproj");
        return projects.Count == 1 ? projects[0] : null;
    }

    private static bool ValidatePositional(string subcommand, string? name, out string? error)
    {
        error = null;
        switch (subcommand)
        {
            case "add" when string.IsNullOrWhiteSpace(name):
                error = "'rask db add' needs a migration name, e.g. rask db add InitialCreate.";
                return false;
            case "restore" when string.IsNullOrWhiteSpace(name):
                error = "'rask db restore' needs the backup file to restore, e.g. rask db restore app-20260805-081500.db.";
                return false;
            case "remove" or "list" or "drop" or "backup" when name is not null:
                error = $"'rask db {subcommand}' takes no name argument.";
                return false;
            default:
                // 'update' accepts an optional target migration; the others are already covered above.
                return true;
        }
    }

    /// <summary>
    /// Build the <c>dotnet ef …</c> argument list. Pure and deterministic, so it is unit-tested directly
    /// (like <see cref="DevCommand.BuildDotnetArguments"/>).
    /// </summary>
    internal static IReadOnlyList<string> BuildEfArguments(
        string subcommand,
        string? name,
        string project,
        string startupProject,
        string? context,
        string? output,
        // Named for EF's flag, not ours: this is what becomes `dotnet ef database drop --force`. It is
        // fed from rask's --yes, which is a different question (skip MY prompt) that happens to imply
        // the same answer downstream (skip EF's).
        bool force,
        IReadOnlyList<string> passthrough)
    {
        var args = new List<string> { "ef" };

        switch (subcommand)
        {
            case "add":
                args.Add("migrations");
                args.Add("add");
                args.Add(name!);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    args.Add("--output-dir");
                    args.Add(output);
                }

                break;

            case "remove":
                args.Add("migrations");
                args.Add("remove");
                break;

            case "list":
                args.Add("migrations");
                args.Add("list");
                break;

            case "update":
                args.Add("database");
                args.Add("update");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    args.Add(name);
                }

                break;

            case "drop":
                args.Add("database");
                args.Add("drop");
                if (force)
                {
                    args.Add("--force");
                }

                break;
        }

        args.Add("--project");
        args.Add(project);
        args.Add("--startup-project");
        args.Add(startupProject);

        if (!string.IsNullOrWhiteSpace(context))
        {
            args.Add("--context");
            args.Add(context);
        }

        // Forwarded verbatim (e.g. --verbose, --connection). These are dotnet-ef options, so they are
        // appended as ordinary arguments rather than after a '--' separator (which ef reserves for the app).
        args.AddRange(passthrough);

        return args;
    }
}
