namespace Rask.Cli;

/// <summary>
///     The <c>rask dev --dry-run --json</c> document: the process that would start, where, and the
///     environment laid over the current one — the half of the plan the command line alone hides.
/// </summary>
internal sealed record DevDryRunReport(
    string Command,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment);
