namespace Rask.Cli;

/// <summary>The <c>rask new --dry-run --json</c> document. Paths are relative to where the command ran.</summary>
internal sealed record NewDryRunReport(string Template, string Name, string Directory, IReadOnlyList<string> Files);
