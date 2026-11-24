using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask audit</c> — every stored aggregate asked the rules its type declares today, and each one a rule refuses
/// reported by rule and field, with the ids alone. The app answers it itself: this runs
/// <c>dotnet run -- rask audit</c>, which builds the app's host without starting it and reads its database through its
/// own model — the same command a published app runs as <c>dotnet App.dll rask audit</c> on the server.
/// </summary>
/// <remarks>
/// Read only: the app's audit refuses any save and takes no lock. Its exit code is passed on — 0 every row passes,
/// 1 some do not — so a release step can stop on a rule the stored data would break.
/// </remarks>
internal sealed class AuditCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "audit";

    public override string Summary => "Report every stored aggregate its current rules refuse (reads the database, never writes).";

    public override string Usage => "rask audit [--aggregate <Name>]... [--unique-candidates] [--json] [--project <path>]";

    public override IReadOnlyList<string> Examples =>
    [
        "rask audit",
        "rask audit --aggregate Driver",
        "rask audit --unique-candidates   # before a migration adds a unique index",
        "rask audit --json > audit.json",
        "dotnet App.dll rask audit   # on the server, from the published app",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .MultiOption("aggregate", 'a', "Name", "Ask only this aggregate (repeat, or separate names with commas).")
            .Flag("unique-candidates", description: "Ask the model's unique indexes instead: the stored rows each would refuse, before its migration runs.")
            .Option("project", 'p', "path", "The app's project (default: found from the current directory).")
            .Flag("dry-run", description: "Print what would run without reading the database.")
            .WithJson();

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var parsed = CreateSchema().Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        if (parsed.Positionals.Count > 0)
        {
            return Fail($"'{parsed.Positionals[0]}' isn't an option of `rask audit`. Name an aggregate with --aggregate {parsed.Positionals[0]}.");
        }

        var project = parsed.Option("project") ?? ProjectLocator.Locate(_fileSystem, _workingDirectory)?.ProjectDirectory;
        if (project is null)
        {
            return Fail($"{ProjectLocator.DescribeMissing(_fileSystem, _workingDirectory)} Run this inside the app's project, or pass --project.");
        }

        var dotnetArgs = BuildArguments(project, parsed.MultiOption("aggregate"), parsed.HasFlag("json"), parsed.HasFlag("unique-candidates"));
        if (parsed.HasFlag("dry-run"))
        {
            WriteDryRun("run", "dotnet " + string.Join(' ', dotnetArgs));
            return 0;
        }

        return await _process.RunAsync("dotnet", dotnetArgs, _workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The <c>dotnet run</c> that has the app audit itself. Pure, so tests assert on it without a process.</summary>
    internal static IReadOnlyList<string> BuildArguments(string project, IReadOnlyList<string> aggregates, bool json, bool uniqueCandidates = false)
    {
        // Quiet, so the build says only what failed: with --json, the report is all the output is.
        var args = new List<string> { "run", "--project", project, "--verbosity", "quiet", "--", "rask", "audit" };
        foreach (var aggregate in aggregates)
        {
            args.Add("--aggregate");
            args.Add(aggregate);
        }

        if (uniqueCandidates)
        {
            args.Add("--unique-candidates");
        }

        if (json)
        {
            args.Add("--json");
        }

        return args;
    }
}
