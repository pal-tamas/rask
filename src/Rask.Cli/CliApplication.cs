using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli;

/// <summary>
/// The tool's top-level router: it resolves the first token to a <see cref="CliCommand"/>, handles the
/// built-in <c>help</c> / <c>--version</c> verbs, and prints usage. Everything is injectable so the whole
/// dispatch is unit-testable without a real console or process.
/// </summary>
internal sealed class CliApplication
{
    private readonly IConsole _console;
    private readonly IReadOnlyList<CliCommand> _commands;

    public CliApplication(IConsole console, IReadOnlyList<CliCommand> commands)
    {
        _console = console;
        _commands = commands;
    }

    /// <summary>Wire the real command set with production collaborators.</summary>
    public static CliApplication CreateDefault(IConsole console, IProcessRunner process, IFileSystem fileSystem) =>
        new(console,
        [
            new NewCommand(console, fileSystem, process, Environment.CurrentDirectory),
            new DevCommand(console, process),
            new GenerateCommand(console, fileSystem, process, Environment.CurrentDirectory),
            new DbCommand(console, fileSystem, process, Environment.CurrentDirectory),
            new DeployCommand(console, fileSystem, process, Environment.CurrentDirectory),
            new InfoCommand(console, process),
        ]);

    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (args.Count == 0)
        {
            PrintUsage(_console.Out);
            return 0;
        }

        var first = args[0];

        if (first is "-h" or "--help" or "help")
        {
            if (args.Count > 1 && TryGetCommand(args[1], out var helpTarget))
            {
                PrintCommandHelp(_console.Out, helpTarget);
            }
            else
            {
                PrintUsage(_console.Out);
            }

            return 0;
        }

        if (first is "-v" or "--version" or "version")
        {
            _console.Out.WriteLine(CliMetadata.Version);
            return 0;
        }

        if (!TryGetCommand(first, out var command))
        {
            _console.Error.WriteLine($"Unknown command '{first}'.");
            PrintUsage(_console.Error);
            return 1;
        }

        var rest = args.Skip(1).ToArray();
        if (RequestsHelp(rest))
        {
            PrintCommandHelp(_console.Out, command);
            return 0;
        }

        return await command.ExecuteAsync(rest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// True if a top-level <c>--help</c>/<c>-h</c> precedes any <c>--</c> passthrough separator. Tokens
    /// after <c>--</c> belong to the user's app (e.g. <c>rask dev -- --help</c>), not to the CLI.
    /// </summary>
    private static bool RequestsHelp(IReadOnlyList<string> args)
    {
        foreach (var token in args)
        {
            if (token == "--")
            {
                break;
            }

            if (token is "--help" or "-h")
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetCommand(string name, out CliCommand command)
    {
        foreach (var candidate in _commands)
        {
            if (candidate.Name.Equals(name, StringComparison.Ordinal) || candidate.Aliases.Contains(name))
            {
                command = candidate;
                return true;
            }
        }

        command = null!;
        return false;
    }

    private void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("rask — the Rask framework command-line tool");
        writer.WriteLine();
        writer.WriteLine("Usage: rask <command> [options]");
        writer.WriteLine();
        writer.WriteLine("Commands:");

        var width = _commands.Count == 0 ? 0 : _commands.Max(c => c.Name.Length);
        foreach (var command in _commands)
        {
            writer.WriteLine($"  {command.Name.PadRight(width)}   {command.Summary}");
        }

        writer.WriteLine();
        writer.WriteLine("Run 'rask <command> --help' for command-specific usage.");
        writer.WriteLine("  --version    Show the tool version.");
    }

    private static void PrintCommandHelp(TextWriter writer, CliCommand command)
    {
        writer.WriteLine(command.Summary);
        writer.WriteLine();
        writer.WriteLine($"Usage: {command.Usage}");
    }
}
