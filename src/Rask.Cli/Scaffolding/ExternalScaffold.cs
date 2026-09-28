namespace Rask.Cli.Scaffolding;

/// <summary>
/// What a generator produced: the <see cref="Files"/> to write, optional <see cref="Notes"/> to print
/// afterwards (e.g. a template's "run a migration" next steps), and the NuGet <see cref="Packages"/> the
/// output needs — the command adds them to the project automatically.
/// </summary>
/// <remarks>
/// This carried eight more members when <c>rask generate</c> existed: the DbContext splice points, the
/// <c>Program.cs</c> registrations, and the sibling test project a <c>--tests</c> run wired up. They went
/// with the command that produced them. Nothing warns about an unused property on an internal record, so
/// they would have sat here looking like part of the design.
/// </remarks>
/// <summary>
///     A command run before the scaffold's own files are written, to produce something Rask does not own.
/// </summary>
/// <param name="Command">The executable, e.g. <c>npx</c>.</param>
/// <param name="Arguments">Passed through <c>ArgumentList</c>, never concatenated into a command line.</param>
/// <param name="Description">What it is doing, printed before it runs so a slow download is explained.</param>
/// <param name="MissingHint">What to install, and how, when <paramref name="Command" /> is not on PATH.</param>
/// <remarks>
///     The front-end templates use this to run the framework's own scaffolder — <c>create-vite</c> — rather
///     than shipping a copy of it. A React skeleton Rask maintained by hand would be a worse React skeleton
///     within a release or two, and it is not what a React developer would recognise. The cost is honest and
///     stated: <c>rask new --template react</c> needs node and a network, where the C# templates need
///     neither.
/// </remarks>
internal sealed record ExternalScaffold(
    string Command,
    IReadOnlyList<string> Arguments,
    string Description,
    string MissingHint)
{
    /// <summary>
    ///     A directory under the target to run the command in, created first. Empty means the target
    ///     itself, which is what every scaffolder that accepts a nested path uses.
    /// </summary>
    /// <remarks>
    ///     For creators that will only take a single path segment. <c>create-analog</c> is the one:
    ///     given <c>Shop/client</c> it stops and asks for a package name — for ANY nested path, lower
    ///     case included — and a prompt inside <c>rask new</c> is a hang rather than a failure anyone can
    ///     act on. Run from inside <c>Shop</c> with a target of <c>client</c>, it completes.
    /// </remarks>
    public string WorkingSubdirectory { get; init; } = string.Empty;

    /// <summary>
    ///     The directory the creator writes its app into, relative to where it runs.
    /// </summary>
    /// <remarks>
    ///     Named rather than inferred from the arguments. Inferring it means guessing which bare token
    ///     is the target, and the guess is wrong the moment a flag takes a value — <c>--template
    ///     angular-v20</c> looks exactly like a directory. Empty means the scaffold has nothing to tidy
    ///     inside.
    /// </remarks>
    public string CreatedDirectory { get; init; } = string.Empty;
}
