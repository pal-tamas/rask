namespace Rask.Cli.Scaffolding;

/// <summary>
///     An in-place edit to a file the scaffold did not write.
/// </summary>
/// <remarks>
///     For the handful of places where an external scaffolder's output has to be amended rather than
///     replaced — a dependency added to its <c>package.json</c>, a line added to its <c>.gitignore</c>.
///     Overwriting those wholesale would mean carrying a copy of exactly the file we chose not to own.
/// </remarks>
internal sealed record ScaffoldPatch(string Path, Func<string, string> Transform, string Description);
