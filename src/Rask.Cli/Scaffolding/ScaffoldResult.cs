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
    ///     given <c>Shop/Client</c> it stops and asks for a package name — for ANY nested path, lower
    ///     case included — and a prompt inside <c>rask new</c> is a hang rather than a failure anyone can
    ///     act on. Run from inside <c>Shop</c> with a target of <c>Client</c>, it completes.
    /// </remarks>
    public string WorkingSubdirectory { get; init; } = string.Empty;
}

/// <summary>
///     An in-place edit to a file the scaffold did not write.
/// </summary>
/// <remarks>
///     For the handful of places where an external scaffolder's output has to be amended rather than
///     replaced — a dependency added to its <c>package.json</c>, a line added to its <c>.gitignore</c>.
///     Overwriting those wholesale would mean carrying a copy of exactly the file we chose not to own.
/// </remarks>
internal sealed record ScaffoldPatch(string Path, Func<string, string> Transform, string Description);

internal sealed record ScaffoldResult(IReadOnlyList<ScaffoldFile> Files, string? Notes = null)
{
    /// <summary>Commands run before <see cref="Files" /> are written, in order.</summary>
    public IReadOnlyList<ExternalScaffold> ExternalScaffolds { get; init; } = [];

    /// <summary>Edits applied after <see cref="Files" /> are written, in order.</summary>
    public IReadOnlyList<ScaffoldPatch> Patches { get; init; } = [];

    /// <summary>Packages the generated code references, added to the project via <c>dotnet add package</c>.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>
    /// The project/solution the command should restore (and guard against overwriting), relative to the target
    /// directory. <c>null</c> means the single-project default (<c>{name}.csproj</c> at the target root). A
    /// multi-project template — a front-end one, with its client beside an ASP.NET host — sets this to its
    /// <c>{name}.slnx</c>, which has no root csproj.
    /// </summary>
    public string? RestoreTarget { get; init; }

    public static ScaffoldResult Single(ScaffoldFile file) => new([file]);
}
