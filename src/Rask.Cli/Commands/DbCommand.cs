using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask db</c> — the EF Core migration lifecycle behind friendly verbs. Each subcommand wraps a
/// <c>dotnet ef</c> command (<c>add</c>/<c>remove</c>/<c>list</c> → <c>migrations …</c>,
/// <c>update</c>/<c>drop</c> → <c>database …</c>), auto-resolving the target project from the current
/// directory and installing the <c>dotnet-ef</c> tool on first use if it's missing. Anything after
/// <c>--</c> is forwarded to <c>dotnet ef</c> verbatim (an escape hatch for <c>--verbose</c> etc.).
/// </summary>
internal sealed partial class DbCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private static readonly string[] Subcommands = ["add", "remove", "list", "update", "drop", "backup", "restore"];

    // These two never touch EF: they copy a file through SQLite (locally) or through a container on
    // the host, so they must not pay for a dotnet-ef install, and must work on an app that has none.
    private static readonly string[] FileSubcommands = ["backup", "restore"];

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "db";

    public override string Summary => "Manage EF Core migrations, and back the database up or restore it.";

    public override string Usage =>
        "rask db <add|remove|list|update|drop|backup|restore> [<name|file>] [--project <path>] [--startup-project <path>] [--context <Name>] [--output <path>] [--remote] [--host <user@host>] [--app <name>] [--force] [-- <ef args>]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
    [
        ("<add|remove|list|update|drop|backup|restore>", "The action to run."),
        ("[<name|file>]", "Migration name for 'add' (e.g. InitialCreate), or the backup file for 'restore'."),
    ];

    public override IReadOnlyList<string> Examples =>
    [
        "rask db add InitialCreate",
        "rask db update",
        "rask db list",
        "rask db drop --force",
        "rask db backup",
        "rask db backup --remote --output backups/",
        "rask db restore backups/shop-20260805-081500.db --remote",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Option("project", 'p', "path", "Project containing the DbContext (default: found from the current directory).")
            .Option("startup-project", 's', "path", "Startup project (default: same as --project).")
            .Option("context", 'c', "Name", "DbContext to use when the project defines more than one.")
            .Option("output", 'o', "path", "Migration output directory (add), or the backup file or directory to write (backup).")
            .Flag("remote", null, "Act on the deployed database instead of the local one (backup, restore).")
            .Option("host", null, "user@host", "Deployment host for --remote (default: the one in .rask/deploy.json).")
            .Option("app", null, "name", "Deployed app name for --remote (default: the one in .rask/deploy.json).")
            .Flag("force", 'f', "Skip the confirmation prompt (drop, restore).");

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            Console.Error.WriteLine($"Specify a migration action: {string.Join(", ", Subcommands)}.");
            Console.Error.WriteLine($"Usage: {Usage}");
            return 1;
        }

        var subcommand = args[0];
        if (!Subcommands.Contains(subcommand))
        {
            Console.Error.WriteLine($"Unknown action '{subcommand}'. Use one of: {string.Join(", ", Subcommands)}.");
            return 1;
        }

        var schema = CreateSchema();

        var parsed = schema.Parse(args.Skip(1).ToArray());
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        if (parsed.Positionals.Count > 1)
        {
            Console.Error.WriteLine($"Unexpected argument '{parsed.Positionals[1]}'. Usage: {Usage}");
            return 1;
        }

        var name = parsed.Positionals.FirstOrDefault();
        if (!ValidatePositional(subcommand, name, out var positionalError))
        {
            Console.Error.WriteLine(positionalError);
            return 1;
        }

        var output = parsed.Option("output");
        if (output is not null && subcommand is not ("add" or "backup"))
        {
            Console.Error.WriteLine("--output only applies to 'rask db add' and 'rask db backup'.");
            return 1;
        }

        if (parsed.HasFlag("force") && subcommand is not ("drop" or "restore"))
        {
            Console.Error.WriteLine("--force only applies to 'rask db drop' and 'rask db restore'.");
            return 1;
        }

        var remote = parsed.HasFlag("remote");
        foreach (var option in new[] { "remote", "host", "app" })
        {
            var supplied = option == "remote" ? remote : parsed.Option(option) is not null;
            if (supplied && !FileSubcommands.Contains(subcommand))
            {
                Console.Error.WriteLine($"--{option} only applies to 'rask db backup' and 'rask db restore'.");
                return 1;
            }
        }

        if (!remote && (parsed.Option("host") is not null || parsed.Option("app") is not null))
        {
            Console.Error.WriteLine("--host and --app only apply with --remote.");
            return 1;
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
                Console.Error.WriteLine($"Couldn't find a single .csproj at or above '{_workingDirectory}'. Run this inside a project, or pass --project.");
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
            // with VACUUM INTO in a sidecar. Neither has a meaning on a client-server database, and there is
            // no half-working version worth shipping: a "backup" that quietly did nothing is the worst
            // possible outcome for a backup command.
            var database = DatabaseCatalog.For(ProjectLocator.Locate(_fileSystem, project)?.Provider ?? DatabaseProvider.Sqlite);
            if (!database.IsFileBased)
            {
                Console.WriteErrorLine(
                    $"`rask db {subcommand}` works on a SQLite file, and this app uses {database.ShortName}.",
                    ConsoleStyle.Error);
                Console.Error.WriteLine(
                    database.Provider == DatabaseProvider.Postgres
                        ? "    Use `pg_dump` / `pg_restore`, or your provider's snapshots. See docs/databases.md."
                        : "    Use your provider's own backup tooling. See docs/databases.md.");
                return 1;
            }

            return await ExecuteFileActionAsync(
                subcommand,
                name,
                project,
                output,
                remote,
                parsed.Option("host"),
                parsed.Option("app"),
                parsed.HasFlag("force"),
                cancellationToken).ConfigureAwait(false);
        }

        // `--force`'s own help has always said it "skips the confirmation prompt" — and there was no
        // prompt. `dotnet ef database drop` does its own, but only when it has a terminal, so a drop run
        // from a script destroyed the database with nothing asked. Ask here, where we know the answer
        // matters, and refuse rather than guess when there's nobody to ask.
        if (subcommand == "drop" && !parsed.HasFlag("force"))
        {
            if (Console.IsInputRedirected)
            {
                Console.WriteErrorLine("`rask db drop` deletes the database. Pass --force to confirm — there's no terminal to ask on.", ConsoleStyle.Error);
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

        var efArgs = BuildEfArguments(subcommand, name, project, startupProject, parsed.Option("context"), output, parsed.HasFlag("force"), parsed.Passthrough);
        return await _process.RunAsync("dotnet", efArgs, _workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    // The EF Core tools need the startup project to reference Microsoft.EntityFrameworkCore.Design;
    // without it `dotnet ef` fails with a terse message. Projects from `rask generate feature` already
    // include it, but a hand-built one (or a demo that uses EnsureCreated) may not — so add it for the
    // user (like `rask generate` does for the packages it needs, and like the dotnet-ef tool install
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

        Console.Out.WriteLine("Adding Microsoft.EntityFrameworkCore.Design to the startup project (required by the EF Core tools)…");
        var exit = await _process.RunAsync("dotnet", ["add", csproj, "package", "Microsoft.EntityFrameworkCore.Design"], _workingDirectory, cancellationToken).ConfigureAwait(false);
        if (exit != 0)
        {
            Console.Error.WriteLine($"  Couldn't add it automatically — add it manually: dotnet add \"{csproj}\" package Microsoft.EntityFrameworkCore.Design");
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
