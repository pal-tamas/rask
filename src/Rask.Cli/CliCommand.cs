namespace Rask.Cli;

/// <summary>
/// A single <c>rask</c> subcommand. Every command needs the <see cref="Console"/>; each declares any
/// further collaborators it needs (an <see cref="IProcessRunner"/>, an <see cref="IFileSystem"/>) in its
/// own constructor, so tests can substitute fakes. Commands parse their own arguments from the tail
/// passed to <see cref="ExecuteAsync"/>.
/// </summary>
internal abstract class CliCommand(IConsole console)
{
    protected IConsole Console { get; } = console;

    /// <summary>The verb the user types, e.g. <c>new</c>.</summary>
    public abstract string Name { get; }

    /// <summary>Short aliases that also resolve to this command, e.g. <c>g</c> for <c>generate</c>.</summary>
    public virtual IReadOnlyList<string> Aliases => [];

    /// <summary>A one-line description shown in the top-level help.</summary>
    public abstract string Summary { get; }

    /// <summary>A one-line usage string shown in the command's own help and on errors.</summary>
    public abstract string Usage { get; }

    /// <summary>Positional arguments (name + description) documented in <c>--help</c>. Empty by default.</summary>
    public virtual IReadOnlyList<(string Name, string Description)> Arguments => [];

    /// <summary>Copy-pasteable example invocations shown in <c>--help</c>. Empty by default.</summary>
    public virtual IReadOnlyList<string> Examples => [];

    /// <summary>
    /// The command's flag/option schema, used by <c>--help</c> to render the options table. Commands that
    /// take options override this to return the same schema they parse with, so help never drifts.
    /// </summary>
    public virtual ArgumentSchema? OptionSchema => null;

    /// <summary>Run the command with the arguments that follow its name. Returns a process exit code.</summary>
    public abstract Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken);

    /// <summary>Report parse errors to stderr and return the conventional usage exit code.</summary>
    protected int Fail(IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
        {
            Console.WriteErrorLine(error, ConsoleStyle.Error);
        }

        Console.Error.WriteLine($"Usage: {Usage}");
        Console.Error.WriteLine($"Run 'rask {Name} --help' for details.");
        return 1;
    }
}
