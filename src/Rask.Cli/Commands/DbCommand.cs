using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask db</c> — the EF Core migration lifecycle behind friendly verbs. Each subcommand wraps a
/// <c>dotnet ef</c> command (<c>add</c>/<c>remove</c>/<c>list</c> → <c>migrations …</c>,
/// <c>update</c>/<c>drop</c> → <c>database …</c>), auto-resolving the target project from the current
/// directory and installing the <c>dotnet-ef</c> tool on first use if it's missing. Anything after
/// <c>--</c> is forwarded to <c>dotnet ef</c> verbatim (an escape hatch for <c>--verbose</c> etc.).
/// </summary>
internal sealed class DbCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private static readonly string[] Subcommands = ["add", "remove", "list", "update", "drop"];

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "db";

    public override string Summary => "Manage EF Core migrations (add, remove, list, update, drop).";

    public override string Usage =>
        "rask db <add|remove|list|update|drop> [<name>] [--project <path>] [--startup-project <path>] [--context <Name>] [--output <dir>] [--force] [-- <ef args>]";

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

        var schema = new ArgumentSchema()
            .Option("project", 'p')
            .Option("startup-project", 's')
            .Option("context", 'c')
            .Option("output", 'o')
            .Flag("force", 'f');

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
        if (output is not null && subcommand != "add")
        {
            Console.Error.WriteLine("--output only applies to 'rask db add'.");
            return 1;
        }

        if (parsed.HasFlag("force") && subcommand != "drop")
        {
            Console.Error.WriteLine("--force only applies to 'rask db drop'.");
            return 1;
        }

        // An explicit --project wins; otherwise fall back to the single .csproj at or above the CWD. EF
        // accepts a directory here and finds the project inside it, so the located directory is enough.
        var project = parsed.Option("project");
        if (project is null)
        {
            var located = ProjectLocator.Locate(_fileSystem, _workingDirectory);
            if (located is null)
            {
                Console.Error.WriteLine($"Couldn't find a single .csproj at or above '{_workingDirectory}'. Run this inside a project, or pass --project.");
                return 1;
            }

            project = located.ProjectDirectory;
        }

        // The startup project configures the DbContext (DI); in a typical single-project Rask app it's the
        // same project that owns the migrations, so it defaults to --project.
        var startupProject = parsed.Option("startup-project") ?? project;

        if (!await EfToolProbe.EnsureAsync(_process, Console, cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        WarnIfDesignPackageMissing(startupProject);

        var efArgs = BuildEfArguments(subcommand, name, project, startupProject, parsed.Option("context"), output, parsed.HasFlag("force"), parsed.Passthrough);
        return await _process.RunAsync("dotnet", efArgs, _workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    // The EF Core tools need the startup project to reference Microsoft.EntityFrameworkCore.Design;
    // without it `dotnet ef` fails with a terse message. Projects from `rask generate feature` already
    // include it, but a hand-built one (or a demo that uses EnsureCreated) may not — so surface the exact
    // fix up front. This only warns and never blocks: if we can't confidently read the startup project's
    // csproj, we stay quiet rather than nag a setup we can't see (e.g. the ref comes from imported props).
    private void WarnIfDesignPackageMissing(string startupProject)
    {
        try
        {
            var csproj = ResolveCsproj(startupProject);
            if (csproj is null || _fileSystem.ReadAllText(csproj).Contains("Microsoft.EntityFrameworkCore.Design", StringComparison.Ordinal))
            {
                return;
            }

            Console.Out.WriteLine("Note: the EF Core tools need the startup project to reference Microsoft.EntityFrameworkCore.Design.");
            Console.Out.WriteLine($"      If this fails, add it with: dotnet add \"{csproj}\" package Microsoft.EntityFrameworkCore.Design");
        }
        catch (IOException)
        {
            // Unreadable project file — skip the hint rather than fail the command over it.
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
            case "remove" or "list" or "drop" when name is not null:
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
