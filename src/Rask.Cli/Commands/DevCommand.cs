namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask dev</c> — run the app in a fast edit loop. Defaults to <c>dotnet watch run</c> (C# Hot Reload
/// on save); <c>--no-hot-reload</c> falls back to a plain <c>dotnet run</c>. Anything after <c>--</c> is
/// forwarded to the app.
/// </summary>
internal sealed class DevCommand(IConsole console, IProcessRunner process) : CliCommand(console)
{
    private readonly IProcessRunner _process = process;

    public override string Name => "dev";

    public override string Summary => "Run the app with hot reload (dotnet watch).";

    public override string Usage => "rask dev [--project <path>] [--no-hot-reload] [-- <args passed to the app>]";

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = new ArgumentSchema()
            .Option("project", 'p')
            .Flag("no-hot-reload");

        var parsed = schema.Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        var dotnetArgs = BuildDotnetArguments(parsed.Option("project"), parsed.HasFlag("no-hot-reload"), parsed.Passthrough);
        return await _process.RunAsync("dotnet", dotnetArgs, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Build the <c>dotnet watch/run</c> argument list. Pure and deterministic, so it is unit-tested directly.</summary>
    internal static IReadOnlyList<string> BuildDotnetArguments(string? project, bool noHotReload, IReadOnlyList<string> passthrough)
    {
        var args = new List<string>();

        if (noHotReload)
        {
            args.Add("run");
            AddProject(args, project);
        }
        else
        {
            args.Add("watch");
            AddProject(args, project);
            args.Add("run");
        }

        if (passthrough.Count > 0)
        {
            args.Add("--");
            args.AddRange(passthrough);
        }

        return args;
    }

    private static void AddProject(List<string> args, string? project)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            args.Add("--project");
            args.Add(project);
        }
    }
}
