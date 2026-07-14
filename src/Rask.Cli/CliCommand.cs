namespace Rask.Cli;

/// <summary>
/// A single <c>rask</c> subcommand. Commands receive their collaborators (<see cref="Console"/> and
/// <see cref="Process"/>) by constructor injection so tests can substitute fakes, and parse their own
/// arguments from the tail passed to <see cref="ExecuteAsync"/>.
/// </summary>
internal abstract class CliCommand(IConsole console, IProcessRunner process)
{
    protected IConsole Console { get; } = console;

    protected IProcessRunner Process { get; } = process;

    /// <summary>The verb the user types, e.g. <c>new</c>.</summary>
    public abstract string Name { get; }

    /// <summary>A one-line description shown in the top-level help.</summary>
    public abstract string Summary { get; }

    /// <summary>A one-line usage string shown in the command's own help and on errors.</summary>
    public abstract string Usage { get; }

    /// <summary>Run the command with the arguments that follow its name. Returns a process exit code.</summary>
    public abstract Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken);

    /// <summary>Report parse errors to stderr and return the conventional usage exit code.</summary>
    protected int Fail(IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
        {
            Console.Error.WriteLine(error);
        }

        Console.Error.WriteLine($"Usage: {Usage}");
        return 1;
    }
}
